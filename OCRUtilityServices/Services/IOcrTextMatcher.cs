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
}
