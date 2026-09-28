namespace OCRUtilityServices.Models;

/// <summary>
/// Defines the strategies available for matching OCR text.
/// </summary>
public enum OcrTextMatchMode
{
    /// <summary>
    /// Requires the OCR text to exactly match the target text after trimming
    /// leading and trailing whitespace.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// Removes whitespace from the OCR text and target text, then determines
    /// whether the normalized OCR text contains the normalized target text.
    /// </summary>
    NormalizedContains = 1
}