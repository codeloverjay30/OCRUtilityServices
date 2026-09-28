using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <inheritdoc/>
public sealed class OcrTextMatcher : IOcrTextMatcher
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="result"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetText"/> is null, empty, or whitespace,
    /// or when the OCR result contains invalid line data.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching line exists or multiple matching lines exist.
    /// </exception>
    public OcrTextLine FindUnique(
        OcrResult result,
        string targetText)
    {
        return FindUnique(
            result,
            targetText,
            OcrTextMatchMode.Exact);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="result"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="targetText"/> is null, empty, or whitespace,
    /// or when the OCR result contains invalid line data.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="matchMode"/> is not supported.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching line exists or multiple matching lines exist.
    /// </exception>
    public OcrTextLine FindUnique(
        OcrResult result,
        string targetText,
        OcrTextMatchMode matchMode)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetText);

        if (result.Lines is null)
        {
            throw new ArgumentException(
                "The OCR result must contain a non-null line collection.",
                nameof(result));
        }

        if (matchMode is not OcrTextMatchMode.Exact
            and not OcrTextMatchMode.NormalizedContains)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchMode),
                matchMode,
                "The specified OCR text match mode is not supported.");
        }

        ReadOnlySpan<char> expected =
            targetText.AsSpan().Trim();

        OcrTextLine? matchedLine = null;

        foreach (OcrTextLine line in result.Lines)
        {
            ValidateLine(line, result);

            ReadOnlySpan<char> actual =
                line.Text.AsSpan().Trim();

            bool isMatch = matchMode switch
            {
                OcrTextMatchMode.Exact =>
                    actual.Equals(
                        expected,
                        StringComparison.Ordinal),

                OcrTextMatchMode.NormalizedContains =>
                    ContainsIgnoringWhitespace(
                        actual,
                        expected),

                _ => false
            };

            if (!isMatch)
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

    /// <summary>
    /// Validates an OCR text line before matching.
    /// </summary>
    /// <param name="line">The OCR text line to validate.</param>
    /// <param name="result">
    /// The OCR result containing the line.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when the OCR line or its text is null.
    /// </exception>
    private static void ValidateLine(
        OcrTextLine? line,
        OcrResult result)
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
    }

    /// <summary>
    /// Determines whether the source contains the target while ignoring
    /// whitespace characters in both values.
    /// </summary>
    /// <param name="source">
    /// The source OCR text.
    /// </param>
    /// <param name="target">
    /// The target text.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the normalized source contains the
    /// normalized target; otherwise, <see langword="false"/>.
    /// </returns>
    private static bool ContainsIgnoringWhitespace(
        ReadOnlySpan<char> source,
        ReadOnlySpan<char> target)
    {
        int sourceLength =
            CountNonWhitespace(source);

        int targetLength =
            CountNonWhitespace(target);

        if (targetLength == 0
            || sourceLength < targetLength)
        {
            return false;
        }

        Span<char> normalizedSource =
            sourceLength <= 256
                ? stackalloc char[sourceLength]
                : new char[sourceLength];

        Span<char> normalizedTarget =
            targetLength <= 256
                ? stackalloc char[targetLength]
                : new char[targetLength];

        CopyWithoutWhitespace(
            source,
            normalizedSource);

        CopyWithoutWhitespace(
            target,
            normalizedTarget);

        return normalizedSource.Contains(
            normalizedTarget,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts the non-whitespace characters in the specified text.
    /// </summary>
    /// <param name="value">
    /// The text to inspect.
    /// </param>
    /// <returns>
    /// The number of non-whitespace characters.
    /// </returns>
    private static int CountNonWhitespace(
        ReadOnlySpan<char> value)
    {
        int count = 0;

        foreach (char character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Copies non-whitespace characters into the destination buffer.
    /// </summary>
    /// <param name="source">
    /// The source text.
    /// </param>
    /// <param name="destination">
    /// The destination buffer.
    /// </param>
    private static void CopyWithoutWhitespace(
        ReadOnlySpan<char> source,
        Span<char> destination)
    {
        int destinationIndex = 0;

        foreach (char character in source)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            destination[destinationIndex++] =
                character;
        }
    }
}