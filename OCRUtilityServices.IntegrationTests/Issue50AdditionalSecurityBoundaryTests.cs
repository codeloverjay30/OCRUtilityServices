using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue50AdditionalSecurityBoundaryTests
{
    private const string ReviewerId = "jay30";
    private const string KeyId = "issue50-key-20261021";
    private static readonly string Review = new('A', 64);
    private static readonly string Annotation = new('B', 64);
    private static readonly string Screenshot = new('C', 64);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 40, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_WrongPublicKey_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer);
        var verifier = CreateVerifier(other);

        Action act = () => Verify(verifier, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public void Verify_RevokedKey_ShouldRejectEvenValidSignature()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer);
        var verifier = CreateVerifier(signer, revoked: true);

        Action act = () => Verify(verifier, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signer is not trusted or has been revoked*");
    }

    [Fact]
    public void Verify_TamperedApproval_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer);
        var tampered = signed with { ExpiresAtUtc = signed.ExpiresAtUtc.AddSeconds(-1) };

        Action act = () => Verify(CreateVerifier(signer), tampered);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public void Verify_ExpiredApproval_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer, Now.AddMinutes(-1), Now);

        Action act = () => Verify(CreateVerifier(signer), signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval is expired or outside its validity period*");
    }

    [Fact]
    public void Verify_RepeatedReadOnlyVerification_ShouldRemainAllowed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer);
        var verifier = CreateVerifier(signer);

        Action act = () =>
        {
            Verify(verifier, signed);
            Verify(verifier, signed);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Verify_WrongExpectedReviewDigest_ShouldReject()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = Sign(signer);

        Action act = () => CreateVerifier(signer).Verify(
            signed, new string('F', 64), Annotation, Screenshot);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval evidence digests do not match trusted evidence*");
    }

    private static TrustedReviewApprovalEvidence Sign(
        ECDsa signer, DateTimeOffset? approvedAt = null, DateTimeOffset? expiresAt = null)
    {
        var unsigned = new TrustedReviewApprovalEvidence(
            ReviewerId, KeyId, Review, Annotation, Screenshot,
            approvedAt ?? Now.AddMinutes(-1), expiresAt ?? Now.AddMinutes(5), string.Empty);
        byte[] signature = signer.SignData(
            TrustedReviewApprovalVerifier.CreateSignedPayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static TrustedReviewApprovalVerifier CreateVerifier(ECDsa key, bool revoked = false) =>
        new(new[] { new TrustedReviewerKey(
            ReviewerId, KeyId, key.ExportSubjectPublicKeyInfoPem(), revoked) },
            new FixedTimeProvider());

    private static void Verify(
        TrustedReviewApprovalVerifier verifier, TrustedReviewApprovalEvidence signed) =>
        verifier.Verify(signed, Review, Annotation, Screenshot);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
