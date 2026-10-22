using System.IO.Abstractions;
using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue50ResignedPayloadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 40, 0, TimeSpan.Zero);
    private const string Review = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Annotation = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Screenshot = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    [Fact]
    public void TryClaim_SamePayloadWithDifferentValidSignatures_ShouldRejectSecondClaim()
    {
        using var fixture = new ClaimFixture();
        using ECDsa firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unsigned = CreateUnsignedApproval();
        var first = Sign(unsigned, firstKey);
        var second = Sign(unsigned, secondKey);

        first.SignatureBase64.Should().NotBe(second.SignatureBase64);
        Verify(first, firstKey);
        Verify(second, secondKey);

        IApprovalConsumptionStore store = fixture.CreateStore();
        store.TryClaim(first).Should().BeTrue();
        store.TryClaim(second).Should().BeFalse();
    }

    [Fact]
    public void TryClaim_AfterStoreRecreation_ShouldStillRejectResignedPayload()
    {
        using var fixture = new ClaimFixture();
        using ECDsa firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unsigned = CreateUnsignedApproval();
        var first = Sign(unsigned, firstKey);
        var second = Sign(unsigned, secondKey);

        Verify(first, firstKey);
        Verify(second, secondKey);
        fixture.CreateStore().TryClaim(first).Should().BeTrue();

        fixture.CreateStore().TryClaim(second).Should().BeFalse();
    }

    private static TrustedReviewApprovalEvidence CreateUnsignedApproval() =>
        new("jay30", "issue50-key-20261021", Review, Annotation, Screenshot,
            Now.AddMinutes(-1), Now.AddMinutes(5), string.Empty);

    private static TrustedReviewApprovalEvidence Sign(
        TrustedReviewApprovalEvidence approval, ECDsa key)
    {
        byte[] signature = key.SignData(
            TrustedReviewApprovalVerifier.CreateSignedPayload(approval),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return approval with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static void Verify(TrustedReviewApprovalEvidence approval, ECDsa key)
    {
        var verifier = new TrustedReviewApprovalVerifier(
            new[] { new TrustedReviewerKey(
                approval.ReviewerId, approval.KeyId, key.ExportSubjectPublicKeyInfoPem()) },
            new FixedTimeProvider());
        verifier.Verify(approval, Review, Annotation, Screenshot);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ClaimFixture : IDisposable
    {
        private readonly IFileSystem _fileSystem = new FileSystem();
        private readonly string _directory;

        public ClaimFixture()
        {
            _directory = _fileSystem.Path.Combine(
                _fileSystem.Path.GetTempPath(),
                "issue50-resigned-" + Guid.NewGuid().ToString("N"));
            _fileSystem.Directory.CreateDirectory(_directory);
        }

        public IApprovalConsumptionStore CreateStore() =>
            new FileApprovalConsumptionStore(_fileSystem, _directory);

        public void Dispose()
        {
            if (_fileSystem.Directory.Exists(_directory))
                _fileSystem.Directory.Delete(_directory, recursive: true);
        }
    }
}
