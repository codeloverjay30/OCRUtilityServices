using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Defines a digest-pinned review and geometry verification boundary.
/// </summary>
public interface IReviewIntegrityAuthorizationService
{
    /// <summary>
    /// Validates all evidence before calculating a proposed point without device input.
    /// </summary>
    Task<(double X, double Y)> AuthorizeAsync(
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
