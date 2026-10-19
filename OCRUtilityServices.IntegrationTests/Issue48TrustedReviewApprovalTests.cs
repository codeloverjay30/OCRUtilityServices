using System.Security.Cryptography;
using CoordinateUtilityServices;
using FluentAssertions;
using Moq;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48TrustedReviewApprovalTests
{
    private const string ReviewerId = "reviewer-001";
    private const string KeyId = "review-key-001";
    private static readonly string ReviewHash = new('A', 64);
    private static readonly string AnnotationHash = new('B', 64);
    private static readonly string ScreenshotHash = new('C', 64);
    private static readonly Rectangle DetectedBounds = Rectangle.FromXYWH(556, 917, 51, 18);

    [Fact]
    public void Verify_ValidSignedApproval_ShouldSucceed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence evidence = Sign(signer, CreateEvidence());
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, evidence);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("review")]
    [InlineData("annotation")]
    [InlineData("screenshot")]
    public void Verify_TamperedSignedDigest_ShouldFailClosed(string field)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        TrustedReviewApprovalEvidence tampered = field switch
        {
            "review" => signed with { ReviewSha256 = new string('D', 64) },
            "annotation" => signed with { AnnotationSha256 = new string('D', 64) },
            _ => signed with { ScreenshotSha256 = new string('D', 64) }
        };
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, tampered);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval evidence digests do not match trusted evidence*");
    }

    [Fact]
    public void Verify_TamperedApprovalTime_ShouldRejectInvalidSignature()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        TrustedReviewApprovalEvidence tampered = signed with
        {
            ApprovedAtUtc = signed.ApprovedAtUtc.AddMinutes(1)
        };
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, tampered);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public void Verify_UnknownKey_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        var sut = new TrustedReviewApprovalVerifier(Array.Empty<TrustedReviewerKey>());

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signer is not trusted or has been revoked*");
    }

    [Fact]
    public void Verify_RevokedKey_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        var sut = CreateVerifier(signer, isRevoked: true);

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signer is not trusted or has been revoked*");
    }

    [Fact]
    public void Verify_ReviewerDoesNotMatchTrustedKey_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        var sut = new TrustedReviewApprovalVerifier(new[]
        {
            new TrustedReviewerKey("another-reviewer", KeyId, signer.ExportSubjectPublicKeyInfoPem())
        });

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signer is not trusted or has been revoked*");
    }

    [Fact]
    public void Verify_ExpiredApproval_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence(
            now.AddHours(-2), now.AddHours(-1)));
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval is expired or outside its validity period*");
    }

    [Fact]
    public void Verify_FutureApproval_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence(
            now.AddHours(1), now.AddHours(2)));
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, signed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval is expired or outside its validity period*");
    }

    [Fact]
    public void Verify_MalformedBase64Signature_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence invalid = CreateEvidence() with
        {
            SignatureBase64 = "not-base64!"
        };
        var sut = CreateVerifier(signer);

        Action act = () => Verify(sut, invalid);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*")
            .WithInnerException<FormatException>();
    }

    [Fact]
    public void Verify_SignatureFromDifferentPrivateKey_ShouldFailClosed()
    {
        using ECDsa trustedSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa attackerSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence forged = Sign(attackerSigner, CreateEvidence());
        var sut = CreateVerifier(trustedSigner);

        Action act = () => Verify(sut, forged);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public void Verify_InvalidDigestEncoding_ShouldFailClosed()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sut = CreateVerifier(signer);

        Action act = () => sut.Verify(CreateEvidence(), "invalid", AnnotationHash, ScreenshotHash);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Approval SHA-256 digest must contain 64 hexadecimal characters*");
    }

    [Fact]
    public async Task AuthorizeAsync_InvalidApproval_ShouldNotDelegateToPhase1()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence forged = CreateEvidence() with
        {
            SignatureBase64 = "not-base64!"
        };
        var inner = new Mock<IReviewIntegrityAuthorizationService>(MockBehavior.Strict);
        var sut = new TrustedReviewIntegrityAuthorizationService(CreateVerifier(signer), inner.Object);

        Func<Task> act = () => AuthorizeAsync(sut, forged);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthorizeAsync_ValidApproval_ShouldDelegateExactlyOnceWithoutDeviceInput()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TrustedReviewApprovalEvidence signed = Sign(signer, CreateEvidence());
        var inner = new Mock<IReviewIntegrityAuthorizationService>(MockBehavior.Strict);
        inner.Setup(x => x.AuthorizeAsync(
                "任務", 0.999f, DetectedBounds,
                "review.json", "annotation.json", "screenshot.png",
                ReviewHash, AnnotationHash, ScreenshotHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((581.5, 926d));
        var sut = new TrustedReviewIntegrityAuthorizationService(CreateVerifier(signer), inner.Object);

        (double X, double Y) point = await AuthorizeAsync(sut, signed);

        point.X.Should().Be(581.5);
        point.Y.Should().Be(926);
        inner.Verify(x => x.AuthorizeAsync(
            "任務", 0.999f, DetectedBounds,
            "review.json", "annotation.json", "screenshot.png",
            ReviewHash, AnnotationHash, ScreenshotHash,
            It.IsAny<CancellationToken>()), Times.Once);
        inner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthorizeAsync_CancelledToken_ShouldNotVerifyOrDelegate()
    {
        var verifier = new Mock<ITrustedReviewApprovalVerifier>(MockBehavior.Strict);
        var inner = new Mock<IReviewIntegrityAuthorizationService>(MockBehavior.Strict);
        var sut = new TrustedReviewIntegrityAuthorizationService(verifier.Object, inner.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => AuthorizeAsync(sut, CreateEvidence(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        verifier.VerifyNoOtherCalls();
        inner.VerifyNoOtherCalls();
    }

    private static TrustedReviewApprovalEvidence CreateEvidence(
        DateTimeOffset? approvedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new TrustedReviewApprovalEvidence(
            ReviewerId, KeyId, ReviewHash, AnnotationHash, ScreenshotHash,
            approvedAt ?? now.AddMinutes(-5),
            expiresAt ?? now.AddMinutes(30),
            string.Empty);
    }

    private static TrustedReviewApprovalEvidence Sign(
        ECDsa signer, TrustedReviewApprovalEvidence evidence)
    {
        byte[] payload = TrustedReviewApprovalVerifier.CreateSignedPayload(evidence);
        byte[] signature = signer.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return evidence with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    private static TrustedReviewApprovalVerifier CreateVerifier(
        ECDsa signer, bool isRevoked = false)
        => new(new[]
        {
            new TrustedReviewerKey(ReviewerId, KeyId,
                signer.ExportSubjectPublicKeyInfoPem(), isRevoked)
        });

    private static void Verify(
        TrustedReviewApprovalVerifier verifier, TrustedReviewApprovalEvidence evidence)
        => verifier.Verify(evidence, ReviewHash, AnnotationHash, ScreenshotHash);

    private static Task<(double X, double Y)> AuthorizeAsync(
        TrustedReviewIntegrityAuthorizationService sut,
        TrustedReviewApprovalEvidence evidence,
        CancellationToken cancellationToken = default)
        => sut.AuthorizeAsync(evidence, "任務", 0.999f, DetectedBounds,
            "review.json", "annotation.json", "screenshot.png",
            ReviewHash, AnnotationHash, ScreenshotHash, cancellationToken);
}
