using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Represents a recognized OCR word and its bounds in the source image.
/// </summary>
public sealed record OcrTextWord(
    string Text,
    Rectangle Bounds);
