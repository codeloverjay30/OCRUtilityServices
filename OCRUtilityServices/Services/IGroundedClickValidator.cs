using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Defines the contract for validating OCR-grounded click coordinates
/// against reviewed ground-truth geometry.
/// </summary>
public interface IGroundedClickValidator
{
    /// <summary>
    /// Validates OCR recognition and reviewed ground-truth geometry
    /// before returning a safe click point.
    /// </summary>
    /// <returns>
    /// A validated click point in source-image pixel coordinates.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when OCR recognition or click safety validation fails.
    /// </exception>
    (double X, double Y) Validate(
        string recognizedText,
        float confidence,
        Rectangle detectedTextBounds,
        string expectedText,
        Rectangle groundTruthTextBounds,
        Rectangle groundTruthButtonBounds,
        int imageWidth,
        int imageHeight,
        bool isReviewed,
        double minimumIoU = 0.70,
        double minimumInset = 5);
}