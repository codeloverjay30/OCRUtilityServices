using System.IO.Abstractions;
using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue50ApprovalConsumptionTests
{
    [Fact]
    public void TryClaim_SameApprovalTwice_ShouldRejectSecondAttempt()
    {
        using var fixture = new ClaimFixture();
        var approval = fixture.CreateSignedApproval();
        var store = fixture.CreateStore();

        store.TryClaim(approval).Should().BeTrue();
        store.TryClaim(approval).Should().BeFalse();
    }

    [Fact]
    public void TryClaim_AfterRecreatingStore_ShouldRemainConsumed()
    {
        using var fixture = new ClaimFixture();
        var approval = fixture.CreateSignedApproval();
        fixture.CreateStore().TryClaim(approval).Should().BeTrue();

        fixture.CreateStore().TryClaim(approval).Should().BeFalse();
    }

    [Fact]
    public async Task TryClaim_ConcurrentAttempts_ShouldAllowExactlyOneClaim()
    {
        using var fixture = new ClaimFixture();
        var approval = fixture.CreateSignedApproval();
        var store = fixture.CreateStore();
        var gate = new ManualResetEventSlim(false);
        Task<bool>[] attempts = Enumerable.Range(0, 12).Select(_ =>
            Task.Run(() =>
            {
                gate.Wait();
                return store.TryClaim(approval);
            })).ToArray();

        gate.Set();
        bool[] results = await Task.WhenAll(attempts);

        results.Count(value => value).Should().Be(1);
        results.Count(value => !value).Should().Be(11);
    }

    [Fact]
    public void TryClaim_MissingDirectory_ShouldFailClosed()
    {
        using var fixture = new ClaimFixture();
        string absent = Path.Combine(fixture.Directory, "absent");

        Action act = () => new FileApprovalConsumptionStore(new FileSystem(), absent);

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*Approval claim directory does not exist*");
    }

    private sealed class ClaimFixture : IDisposable
    {
        private readonly IFileSystem _fileSystem = new FileSystem();
        public string Directory { get; } = Path.Combine(
            Path.GetTempPath(), "issue50-claims-" + Guid.NewGuid().ToString("N"));

        public ClaimFixture()
        {
            Directory = _fileSystem.Path.Combine(
                _fileSystem.Path.GetTempPath(),
                "issue50-claims-" + Guid.NewGuid().ToString("N"));

            _fileSystem.Directory.CreateDirectory(Directory);
        }


        public FileApprovalConsumptionStore CreateStore() =>
            new(new FileSystem(), Directory);

        public TrustedReviewApprovalEvidence CreateSignedApproval()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var approval = new TrustedReviewApprovalEvidence(
                "jay30", "issue50-key-20261021",
                new string('A', 64), new string('B', 64), new string('C', 64),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5), string.Empty);
            byte[] signature = key.SignData(
                TrustedReviewApprovalVerifier.CreateSignedPayload(approval),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return approval with { SignatureBase64 = Convert.ToBase64String(signature) };
        }

        public void Dispose()
        {
            if (_fileSystem.Directory.Exists(Directory))
            {
                _fileSystem.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
