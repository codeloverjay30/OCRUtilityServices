using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Enforces signed reviewer approval before delegating to digest-pinned authorization.
/// Does not perform device input.
/// </summary>
public sealed class TrustedReviewIntegrityAuthorizationService : ITrustedReviewIntegrityAuthorizationService
{
    private readonly ITrustedReviewApprovalVerifier _verifier;
    private readonly IReviewIntegrityAuthorizationService _inner;

    /// <summary>
    /// Creates the signed approval authorization boundary.
    /// </summary>
    public TrustedReviewIntegrityAuthorizationService(
        ITrustedReviewApprovalVerifier verifier,
        IReviewIntegrityAuthorizationService inner)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public Task<(double X, double Y)> AuthorizeAsync(
        TrustedReviewApprovalEvidence approval,
        string recognizedText,
        float confidence,
        Rectangle detectedTextBounds,
        string reviewPath,
        string annotationPath,
        string screenshotPath,
        string trustedReviewSha256,
        string trustedAnnotationSha256,
        string trustedScreenshotSha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _verifier.Verify(approval, trustedReviewSha256,
            trustedAnnotationSha256, trustedScreenshotSha256);
        cancellationToken.ThrowIfCancellationRequested();
        return _inner.AuthorizeAsync(
            recognizedText, confidence, detectedTextBounds,
            reviewPath, annotationPath, screenshotPath,
            trustedReviewSha256, trustedAnnotationSha256, trustedScreenshotSha256,
            cancellationToken);
    }
}
