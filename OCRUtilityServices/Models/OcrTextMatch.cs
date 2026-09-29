using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Represents a target-specific OCR text match and its source-image bounds.
/// </summary>
public sealed record OcrTextMatch(
    string Text,
    Rectangle Bounds);
