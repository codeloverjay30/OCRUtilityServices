using System.Security.Cryptography;
using FluentAssertions;
using Moq;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue50ExecutionGateTests
{
    private const string Review = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Annotation = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Screenshot = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 40, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ValidApproval_ShouldVerifyClaimAndDispatchOnce()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence approval = Sign(key);
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        store.Setup(x => x.TryClaim(approval)).Returns(true);
        int dispatches = 0;
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);

        await gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
        {
            Interlocked.Increment(ref dispatches);
            return Task.CompletedTask;
        });

        dispatches.Should().Be(1);
        store.Verify(x => x.TryClaim(approval), Times.Once);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidSignature_ShouldNotClaimOrDispatch()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence approval = Sign(key) with
        {
            SignatureBase64 = Convert.ToBase64String(new byte[64])
        };
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        int dispatches = 0;
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);

        Func<Task> act = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
        {
            Interlocked.Increment(ref dispatches);
            return Task.CompletedTask;
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
        dispatches.Should().Be(0);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyClaimed_ShouldRejectWithoutDispatch()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var approval = Sign(key);
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        store.Setup(x => x.TryClaim(approval)).Returns(false);
        int dispatches = 0;
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);

        Func<Task> act = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
        {
            Interlocked.Increment(ref dispatches);
            return Task.CompletedTask;
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval has already been claimed*");
        dispatches.Should().Be(0);
        store.Verify(x => x.TryClaim(approval), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCalls_ShouldDispatchOnlyOnce()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var approval = Sign(key);
        int claimed = 0;
        int dispatched = 0;
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        store.Setup(x => x.TryClaim(approval))
            .Returns(() => Interlocked.CompareExchange(ref claimed, 1, 0) == 0);
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);
        using var start = new ManualResetEventSlim(false);

        Task[] attempts = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            await gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
            {
                Interlocked.Increment(ref dispatched);
                return Task.CompletedTask;
            });
        })).ToArray();
        start.Set();
        Exception?[] failures = await Task.WhenAll(attempts.Select(async task =>
        {
            try { await task; return (Exception?)null; }
            catch (Exception ex) { return ex; }
        }));

        dispatched.Should().Be(1);
        failures.Count(ex => ex is null).Should().Be(1);
        failures.Where(ex => ex is not null).Should().OnlyContain(
            ex => ex is InvalidOperationException &&
                ex.Message.Contains("Approval has already been claimed", StringComparison.Ordinal));
        store.Verify(x => x.TryClaim(approval), Times.Exactly(12));
    }

    [Fact]
    public async Task ExecuteAsync_ClickTimeout_ShouldKeepClaimAndRejectRetry()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var approval = Sign(key);
        int claims = 0;
        int dispatches = 0;
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        store.Setup(x => x.TryClaim(approval))
            .Returns(() => Interlocked.Increment(ref claims) == 1);
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);
        Func<CancellationToken, Task> click = _ =>
        {
            Interlocked.Increment(ref dispatches);
            return Task.FromException(new TimeoutException("Appium click outcome is unknown."));
        };

        Func<Task> first = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot, click);
        await first.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Appium click outcome is unknown*");

        Func<Task> second = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot, click);
        await second.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval has already been claimed*");
        dispatches.Should().Be(1);
        claims.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeClaim_ShouldNotConsumeApproval()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var approval = Sign(key);
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int dispatches = 0;
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);

        Func<Task> act = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot,
            _ =>
            {
                Interlocked.Increment(ref dispatches);
                return Task.CompletedTask;
            }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        dispatches.Should().Be(0);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_ClaimStoreFailure_ShouldNotDispatch()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var approval = Sign(key);
        var store = new Mock<IApprovalConsumptionStore>(MockBehavior.Strict);
        store.Setup(x => x.TryClaim(approval))
            .Throws(new IOException("Claim store unavailable."));
        int dispatches = 0;
        var gate = new TrustedApprovalExecutionGate(CreateVerifier(key), store.Object);

        Func<Task> act = () => gate.ExecuteAsync(approval, Review, Annotation, Screenshot, _ =>
        {
            Interlocked.Increment(ref dispatches);
            return Task.CompletedTask;
        });

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*Claim store unavailable*");
        dispatches.Should().Be(0);
    }

    private static TrustedReviewApprovalEvidence Sign(ECDsa key)
    {
        var approval = new TrustedReviewApprovalEvidence(
            "jay30", "issue50-key-20261021", Review, Annotation, Screenshot,
            Now.AddMinutes(-1), Now.AddMinutes(5), string.Empty);
        byte[] signature = key.SignData(
            TrustedReviewApprovalVerifier.CreateSignedPayload(approval),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return approval with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static ITrustedReviewApprovalVerifier CreateVerifier(ECDsa key) =>
        new TrustedReviewApprovalVerifier(
            new[] { new TrustedReviewerKey("jay30", "issue50-key-20261021", key.ExportSubjectPublicKeyInfoPem()) },
            new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
