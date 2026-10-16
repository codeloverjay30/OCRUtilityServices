using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Authorizes a click point from a uniquely recognized OCR target and reviewed annotation.
/// </summary>
public interface IGroundedClickAuthorizationService
{
    /// <summary>
    /// Returns a click coordinate only after validation succeeds.
    /// </summary>
    (double X, double Y) Authorize(
        string recognizedText, float confidence,
        CoordinateUtilityServices.Rectangle detectedTextBounds,
        ReviewedGroundTruth groundTruth);
}
