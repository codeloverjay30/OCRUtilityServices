using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Validates a uniquely matched OCR text region against reviewed
/// ground-truth geometry before authorizing a click point.
/// </summary>
public sealed class GroundedClickValidator : IGroundedClickValidator
{
    private readonly ReviewedPaddedTextGeometryPolicy? _paddedTextPolicy;

    /// <summary>
    /// Creates a validator with the legacy IoU policy unless a padded-text policy is explicitly supplied.
    /// </summary>
    public GroundedClickValidator(ReviewedPaddedTextGeometryPolicy? paddedTextPolicy = null)
    {
        _paddedTextPolicy = paddedTextPolicy;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when ground-truth validation or click safety fails.
    /// </exception>
    public (double X, double Y) Validate(
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
        double minimumInset = 5)
    {
        if (!isReviewed)
        {
            throw new InvalidOperationException(
                "Ground truth has not been reviewed.");
        }

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new InvalidOperationException(
                "Source image dimensions are invalid.");
        }

        if (string.IsNullOrWhiteSpace(expectedText) ||
            !string.Equals(
                recognizedText,
                expectedText,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OCR text does not match the expected target.");
        }

        if (!float.IsFinite(confidence) || confidence <= 0.99f)
        {
            throw new InvalidOperationException(
                "OCR confidence is below the required threshold.");
        }

        if (!double.IsFinite(minimumIoU) ||
            minimumIoU <= 0 || minimumIoU > 1 ||
            !double.IsFinite(minimumInset) ||
            minimumInset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumIoU),
                "Ground truth validation thresholds are invalid.");
        }

        ValidateBounds(
            detectedTextBounds, imageWidth, imageHeight);

        ValidateBounds(
            groundTruthTextBounds, imageWidth, imageHeight);

        ValidateBounds(
            groundTruthButtonBounds, imageWidth, imageHeight);

        if (_paddedTextPolicy is null)
        {
            double intersectionLeft = Math.Max(
                detectedTextBounds.TopLeft.X,
                groundTruthTextBounds.TopLeft.X);

            double intersectionTop = Math.Max(
                detectedTextBounds.TopLeft.Y,
                groundTruthTextBounds.TopLeft.Y);

            double intersectionRight = Math.Min(
                detectedTextBounds.BottomRight.X,
                groundTruthTextBounds.BottomRight.X);

            double intersectionBottom = Math.Min(
                detectedTextBounds.BottomRight.Y,
                groundTruthTextBounds.BottomRight.Y);

            double intersectionArea =
                Math.Max(0, intersectionRight - intersectionLeft) *
                Math.Max(0, intersectionBottom - intersectionTop);

            double detectedArea =
                detectedTextBounds.Width * detectedTextBounds.Height;

            double truthArea =
                groundTruthTextBounds.Width * groundTruthTextBounds.Height;

            double unionArea = detectedArea + truthArea - intersectionArea;

            double iou = unionArea > 0
                ? intersectionArea / unionArea
                : 0;

            if (!double.IsFinite(iou) || iou < minimumIoU)
            {
                throw new InvalidOperationException(
                    "OCR text bounds do not sufficiently overlap ground truth.");
            }

        }
        else
        {
            _paddedTextPolicy.Validate(detectedTextBounds, groundTruthTextBounds);
        }

        double centerX =
            detectedTextBounds.TopLeft.X +
            detectedTextBounds.Width / 2.0;

        double centerY =
            detectedTextBounds.TopLeft.Y +
            detectedTextBounds.Height / 2.0;

        if (centerX < groundTruthButtonBounds.TopLeft.X + minimumInset ||
            centerX > groundTruthButtonBounds.BottomRight.X - minimumInset ||
            centerY < groundTruthButtonBounds.TopLeft.Y + minimumInset ||
            centerY > groundTruthButtonBounds.BottomRight.Y - minimumInset)
        {
            throw new InvalidOperationException(
                "OCR text center is outside the safe button area.");
        }

        return (centerX, centerY);
    }

    /// <summary>
    /// Ensures a rectangle is finite, nonempty, and entirely inside
    /// the source image.
    /// </summary>
    private static void ValidateBounds(
        Rectangle bounds,
        int imageWidth,
        int imageHeight)
    {
        double left = bounds.TopLeft.X;
        double top = bounds.TopLeft.Y;
        double right = bounds.BottomRight.X;
        double bottom = bounds.BottomRight.Y;

        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            left < 0 ||
            top < 0 ||
            right <= left ||
            bottom <= top ||
            right > imageWidth ||
            bottom > imageHeight)
        {
            throw new InvalidOperationException(
                "Ground truth or OCR bounds are invalid.");
        }
    }
}