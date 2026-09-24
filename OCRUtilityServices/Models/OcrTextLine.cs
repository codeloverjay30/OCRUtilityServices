using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Represents a recognized text line and its bounds in the source image.
/// </summary>
public sealed record OcrTextLine(
    string Text,
    Rectangle Bounds);