using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Validates OCR text geometry against independently reviewed text bounds that include modest padding.
/// This policy must be explicitly opted into; it does not alter the default IoU validator behavior.
/// </summary>
public sealed class ReviewedPaddedTextGeometryPolicy
{
    /// <summary>
    /// Validates minimum OCR coverage, relative dimensions, and normalized center displacement.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when detected geometry is not consistent with the reviewed text bounds.
    /// </exception>
    public void Validate(Rectangle detected, Rectangle reviewed)
    {
        double intersectionWidth = Math.Max(0,
            Math.Min(detected.BottomRight.X, reviewed.BottomRight.X) -
            Math.Max(detected.TopLeft.X, reviewed.TopLeft.X));
        double intersectionHeight = Math.Max(0,
            Math.Min(detected.BottomRight.Y, reviewed.BottomRight.Y) -
            Math.Max(detected.TopLeft.Y, reviewed.TopLeft.Y));

        double detectedArea = detected.Width * detected.Height;
        double coverage = intersectionWidth * intersectionHeight / detectedArea;
        double widthRatio = detected.Width / reviewed.Width;
        double heightRatio = detected.Height / reviewed.Height;

        double detectedCenterX = detected.TopLeft.X + detected.Width / 2.0;
        double detectedCenterY = detected.TopLeft.Y + detected.Height / 2.0;
        double reviewedCenterX = reviewed.TopLeft.X + reviewed.Width / 2.0;
        double reviewedCenterY = reviewed.TopLeft.Y + reviewed.Height / 2.0;

        double offsetX = Math.Abs(detectedCenterX - reviewedCenterX) / reviewed.Width;
        double offsetY = Math.Abs(detectedCenterY - reviewedCenterY) / reviewed.Height;

        // These are conservative candidate thresholds, not yet E2E-validated.
        if (!double.IsFinite(coverage) || coverage < 0.95 ||
            !double.IsFinite(widthRatio) || widthRatio < 0.75 || widthRatio > 1.10 ||
            !double.IsFinite(heightRatio) || heightRatio < 0.45 || heightRatio > 1.10 ||
            !double.IsFinite(offsetX) || offsetX > 0.10 ||
            !double.IsFinite(offsetY) || offsetY > 0.15)
        {
            throw new InvalidOperationException(
                "OCR text bounds do not satisfy reviewed padded-text geometry policy.");
        }
    }
}
