using System.IO.Abstractions;
using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue54ProductionApprovalClaimConcurrencyTests
{
    private const string Review = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Annotation = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Screenshot = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 40, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ConcurrentIndependentGates_ShouldDispatchExactlyOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var fixture = new ProductionFixture();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence approval = Sign(key);
        const int attemptCount = 16;
        int dispatchCount = 0;
        using var start = new ManualResetEventSlim(false);

        Task<Exception?>[] attempts = Enumerable.Range(0, attemptCount)
            .Select(_ => Task.Run(async () =>
            {
                TrustedApprovalExecutionGate gate = fixture.CreateGate(key);
                start.Wait(timeout.Token);
                try
                {
                    await gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
                    {
                        Interlocked.Increment(ref dispatchCount);
                        return Task.CompletedTask;
                    }, timeout.Token);
                    return (Exception?)null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }, timeout.Token))
            .ToArray();

        start.Set();
        Exception?[] outcomes = await Task.WhenAll(attempts)
            .WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);

        outcomes.Count(exception => exception is null).Should().Be(1);
        outcomes.Count(exception => exception is InvalidOperationException &&
            exception.Message.Contains("Approval has already been claimed.",
                StringComparison.Ordinal)).Should().Be(attemptCount - 1);
        dispatchCount.Should().Be(1);
        fixture.ClaimFileCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhileFirstDispatchIsPending_SecondGateShouldRejectReplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var fixture = new ProductionFixture();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence approval = Sign(key);
        TrustedApprovalExecutionGate firstGate = fixture.CreateGate(key);
        TrustedApprovalExecutionGate secondGate = fixture.CreateGate(key);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int dispatchCount = 0;

        Task first = firstGate.ExecuteAsync(approval, Review, Annotation, Screenshot,
            async token =>
            {
                Interlocked.Increment(ref dispatchCount);
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }, timeout.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10),
                timeout.Token);

            Func<Task> replay = () => secondGate.ExecuteAsync(
                approval, Review, Annotation, Screenshot, _ =>
                {
                    Interlocked.Increment(ref dispatchCount);
                    return Task.CompletedTask;
                }, timeout.Token);

            await replay.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Approval has already been claimed*");
            dispatchCount.Should().Be(1);
            fixture.ClaimFileCount.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10),
                timeout.Token);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DispatchFailure_ShouldPreserveClaimAcrossNewGateAndStore()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var fixture = new ProductionFixture();
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence approval = Sign(key);
        int dispatchCount = 0;

        Func<Task> first = () => fixture.CreateGate(key).ExecuteAsync(
            approval, Review, Annotation, Screenshot, _ =>
            {
                Interlocked.Increment(ref dispatchCount);
                throw new TimeoutException("Appium dispatch outcome is unknown.");
            }, timeout.Token);

        await first.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Appium dispatch outcome is unknown*");

        Func<Task> replay = () => fixture.CreateGate(key).ExecuteAsync(
            approval, Review, Annotation, Screenshot, _ =>
            {
                Interlocked.Increment(ref dispatchCount);
                return Task.CompletedTask;
            }, timeout.Token);

        await replay.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval has already been claimed*");
        dispatchCount.Should().Be(1);
        fixture.ClaimFileCount.Should().Be(1);
    }

    private static TrustedReviewApprovalEvidence Sign(ECDsa key)
    {
        var unsigned = new TrustedReviewApprovalEvidence(
            "issue54-reviewer", "issue54-key", Review, Annotation, Screenshot,
            Now.AddMinutes(-1), Now.AddMinutes(5), string.Empty);
        byte[] payload = TrustedReviewApprovalVerifier.CreateSignedPayload(unsigned);
        try
        {
            byte[] signature = key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private sealed class ProductionFixture : IDisposable
    {
        private readonly IFileSystem _fileSystem = new FileSystem();
        private readonly string _directory;

        public ProductionFixture()
        {
            _directory = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(),
                "issue54-production-claims-" + Guid.NewGuid().ToString("N"));
            _fileSystem.Directory.CreateDirectory(_directory);
        }

        public int ClaimFileCount => _fileSystem.Directory
            .GetFiles(_directory, "*.claimed").Length;

        public TrustedApprovalExecutionGate CreateGate(ECDsa key)
        {
            var verifier = new TrustedReviewApprovalVerifier(
                new[]
                {
                    new TrustedReviewerKey(
                        "issue54-reviewer", "issue54-key",
                        key.ExportSubjectPublicKeyInfoPem())
                },
                new FixedTimeProvider());
            var store = new FileApprovalConsumptionStore(_fileSystem, _directory);
            return new TrustedApprovalExecutionGate(verifier, store);
        }

        public void Dispose()
        {
            if (_fileSystem.Directory.Exists(_directory))
                _fileSystem.Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
