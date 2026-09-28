using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <inheritdoc/>
public class OcrTextMatcher : IOcrTextMatcher
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="result"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetText"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching line exists or multiple matching lines exist.
    /// </exception>
    public OcrTextLine FindUnique(
        OcrResult result,
        string targetText)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetText);

        if (result.Lines is null)
        {
            throw new ArgumentException(
                "The OCR result must contain a non-null line collection.",
                nameof(result));
        }

        ReadOnlySpan<char> expected = targetText.AsSpan().Trim();

        OcrTextLine? matchedLine = null;

        foreach (OcrTextLine line in result.Lines)
        {
            if (line is null)
            {
                throw new ArgumentException(
                    "The OCR result contains a null text line.",
                    nameof(result));
            }

            if (line.Text is null)
            {
                throw new ArgumentException(
                    "The OCR result contains a text line with null text.",
                    nameof(result));
            }

            ReadOnlySpan<char> actual = line.Text.AsSpan().Trim();

            if (!actual.Equals(
                    expected,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (matchedLine is not null)
            {
                throw new InvalidOperationException(
                    $"OCR target '{targetText}' is ambiguous: " +
                    "multiple matching lines were found.");
            }

            matchedLine = line;
        }

        return matchedLine
            ?? throw new InvalidOperationException(
                $"OCR target '{targetText}' was not found.");
    }
}