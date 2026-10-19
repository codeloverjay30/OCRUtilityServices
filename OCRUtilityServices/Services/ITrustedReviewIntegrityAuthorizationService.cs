using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Defines the signed-review authorization boundary for OCR grounded-click proposals.
/// This contract never performs device input.
/// </summary>
public interface ITrustedReviewIntegrityAuthorizationService
{
    /// <summary>
    /// Verifies trusted approval evidence before delegating to digest-pinned authorization.
    /// </summary>
    Task<(double X, double Y)> AuthorizeAsync(
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
        CancellationToken cancellationToken = default);
}