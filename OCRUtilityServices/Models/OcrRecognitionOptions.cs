using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Defines options that control OCR recognition.
/// </summary>
public sealed class OcrRecognitionOptions
{
    /// <summary>
    /// Gets the BCP-47 language tag used for OCR recognition.
    /// </summary>
    public string? LanguageTag { get; init; }

    /// <summary>
    /// Gets the optional recognition region in source-image coordinates.
    /// </summary>
    public Rectangle? Region { get; init; }
}