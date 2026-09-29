using CoordinateUtilityServices;
using System.Runtime.CompilerServices;

namespace OCRUtilityServices.Internal;

/// <summary>
/// Provides geometry operations for OCR recognition regions.
/// </summary>
internal static class OcrRegionGeometry
{
    /// <summary>
    /// Validates that a recognition region is non-empty and fully contained
    /// within the source image.
    /// </summary>
    internal static void ValidateWithinImage(
        Rectangle region,
        uint imageWidth,
        uint imageHeight)
    {
        if (region.Width <= 0
            || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                "The OCR recognition region must have positive width and height.");
        }

        if (region.TopLeft.X < 0
            || region.TopLeft.Y < 0
            || region.BottomRight.X > imageWidth
            || region.BottomRight.Y > imageHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                "The OCR recognition region is outside the source image bounds.");
        }
    }

    /// <summary>
    /// Translates bounds relative to a recognition region into
    /// source-image coordinates.
    /// </summary>
    internal static Rectangle TranslateToSourceCoordinates(
        Rectangle localBounds,
        Rectangle region)
    {
        return Rectangle.FromXYWH(
            region.TopLeft.X + localBounds.TopLeft.X,
            region.TopLeft.Y + localBounds.TopLeft.Y,
            localBounds.Width,
            localBounds.Height);
    }
}