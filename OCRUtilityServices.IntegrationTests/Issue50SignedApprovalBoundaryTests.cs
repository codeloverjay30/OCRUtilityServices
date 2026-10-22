using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue50SignedApprovalBoundaryTests
{
    private const string ReviewerId = "issue50-reviewer";
    private const string KeyId = "issue50-key";
    private static readonly string ReviewDigest = new('A', 64);
    private static readonly string AnnotationDigest = new('B', 64);
    private static readonly string ScreenshotDigest = new('C', 64);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_ValidP256Approval_ShouldSucceed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval());
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, signed);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("reviewer")]
    [InlineData("key")]
    [InlineData("approved")]
    [InlineData("expires")]
    public void Verify_ModifiedSignedIdentityOrValidity_ShouldReject(string field)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval());
        var changed = field switch
        {
            "reviewer" => signed with { ReviewerId = "another-reviewer" },
            "key" => signed with { KeyId = "unknown-key" },
            "approved" => signed with { ApprovedAtUtc = signed.ApprovedAtUtc.AddSeconds(1) },
            "expires" => signed with { ExpiresAtUtc = signed.ExpiresAtUtc.AddSeconds(-1) },
            _ => throw new InvalidOperationException("Unknown test field.")
        };
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, changed);

        if (field is "reviewer" or "key")
        {
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Approval signer is not trusted or has been revoked*");
        }
        else
        {
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Approval signature is invalid*");
        }
    }

    [Fact]
    public void Verify_AtExactExpiration_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval() with { ExpiresAtUtc = Now });
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval is expired or outside its validity period*");
    }

    [Fact]
    public void Verify_AtExactApprovalTime_ShouldSucceed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval() with { ApprovedAtUtc = Now });
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, signed);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("review")]
    [InlineData("annotation")]
    [InlineData("screenshot")]
    public void Verify_DifferentTrustedDigest_ShouldReject(string field)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval());
        var sut = CreateVerifier(signer);
        string replacement = new('D', 64);

        Action act = () => sut.Verify(
            signed,
            field == "review" ? replacement : ReviewDigest,
            field == "annotation" ? replacement : AnnotationDigest,
            field == "screenshot" ? replacement : ScreenshotDigest);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval evidence digests do not match trusted evidence*");
    }

    [Fact]
    public void Verify_TamperedSignatureBytes_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval());
        byte[] signature = Convert.FromBase64String(signed.SignatureBase64);
        signature[0] ^= 0x01;
        var tampered = signed with { SignatureBase64 = Convert.ToBase64String(signature) };
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, tampered);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public void Verify_RepeatedVerification_IsCurrentlyStateless_AndDoesNotProveReplayProtection()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, CreateApproval());
        var sut = CreateVerifier(signer);

        Action act = () =>
        {
            Verify(sut, signed);
            Verify(sut, signed);
        };

        act.Should().NotThrow();
    }

    private static TrustedReviewApprovalEvidence CreateApproval() =>
        new(ReviewerId, KeyId, ReviewDigest, AnnotationDigest, ScreenshotDigest,
            Now.AddMinutes(-5), Now.AddMinutes(5), string.Empty);

    private static TrustedReviewApprovalEvidence Sign(
        ECDsa signer, TrustedReviewApprovalEvidence approval)
    {
        byte[] payload = TrustedReviewApprovalVerifier.CreateSignedPayload(approval);
        byte[] signature = signer.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return approval with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static TrustedReviewApprovalVerifier CreateVerifier(ECDsa signer) =>
        new(new[]
        {
            new TrustedReviewerKey(ReviewerId, KeyId, signer.ExportSubjectPublicKeyInfoPem())
        }, new FixedTimeProvider(Now));

    private static void Verify(
        TrustedReviewApprovalVerifier verifier,
        TrustedReviewApprovalEvidence approval) =>
        verifier.Verify(approval, ReviewDigest, AnnotationDigest, ScreenshotDigest);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
