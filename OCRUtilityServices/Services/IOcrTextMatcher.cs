using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Finds uniquely matching text lines in optical character recognition results.
/// </summary>
public interface IOcrTextMatcher
{
    /// <summary>
    /// Finds exactly one text line that matches the specified text.
    /// </summary>
    /// <param name="result">
    /// The optical character recognition result to search.
    /// </param>
    /// <param name="targetText">
    /// The text to match.
    /// </param>
    /// <returns>
    /// The uniquely matching text line.
    /// </returns>
    public OcrTextLine FindUnique(
        OcrResult result,
        string targetText);

    /// <summary>
    /// Finds exactly one OCR text line that matches the specified target text
    /// using the requested matching strategy.
    /// </summary>
    /// <param name="result">The OCR result to search.</param>
    /// <param name="targetText">The target text to match.</param>
    /// <param name="matchMode">The matching strategy to apply.</param>
    /// <returns>The uniquely matching OCR text line.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="result"/> or <paramref name="targetText"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetText"/> is empty or consists only of whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="matchMode"/> is not supported.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching line is found or when multiple matching lines are found.
    /// </exception>
    OcrTextLine FindUnique(
        OcrResult result,
        string targetText,
        OcrTextMatchMode matchMode);
}
