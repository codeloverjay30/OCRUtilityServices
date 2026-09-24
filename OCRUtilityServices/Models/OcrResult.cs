namespace OCRUtilityServices.Models;

/// <summary>
/// Represents the text and text-line locations recognized from an image.
/// </summary>
public sealed record OcrResult(
    string Text,
    IReadOnlyList<OcrTextLine> Lines);