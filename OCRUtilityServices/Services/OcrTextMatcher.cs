using System.Text;
using CoordinateUtilityServices;
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

    /// <inheritdoc/>
    public OcrTextMatch FindUniqueMatch(
        OcrResult result,
        string targetText,
        OcrTextMatchMode matchMode)
    {
        OcrTextLine matchedLine =
            FindUnique(
                result,
                targetText,
                matchMode);

        if (matchedLine.Words.Count == 0)
        {
            throw new InvalidOperationException(
                $"OCR target '{targetText}' was found, but its word-level geometry is unavailable.");
        }

        string normalizedTarget = NormalizeWithoutWhitespace(targetText);

        if (normalizedTarget.Length == 0)
        {
            throw new ArgumentException(
                "OCR target text must contain at least one non-whitespace character.",
                nameof(targetText));
        }

        OcrTextMatch? matchedTarget = null;

        for (int startIndex = 0;
             startIndex < matchedLine.Words.Count;
             startIndex++)
        {
            StringBuilder candidateBuilder = new();

            for (int endIndex = startIndex;
                 endIndex < matchedLine.Words.Count;
                 endIndex++)
            {
                OcrTextWord word =
                    matchedLine.Words[endIndex]
                    ?? throw new ArgumentException(
                        "The matched OCR line contains a null word.",
                        nameof(result));

                string normalizedWord =
                    NormalizeWithoutWhitespace(word.Text);

                if (normalizedWord.Length == 0)
                {
                    continue;
                }

                candidateBuilder.Append(normalizedWord);

                string candidate = candidateBuilder.ToString();

                if (candidate.Length > normalizedTarget.Length)
                {
                    break;
                }

                if (!normalizedTarget.StartsWith(
                        candidate,
                        StringComparison.Ordinal))
                {
                    break;
                }

                if (!candidate.Equals(
                        normalizedTarget,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Rectangle bounds =
                    CreateUnionBounds(
                        matchedLine.Words,
                        startIndex,
                        endIndex,
                        result);

                OcrTextMatch currentMatch =
                    new(
                        targetText.Trim(),
                        bounds);

                if (matchedTarget is not null)
                {
                    throw new InvalidOperationException(
                        $"OCR target '{targetText}' is ambiguous: " +
                        "multiple word-level matches were found.");
                }

                matchedTarget = currentMatch;

                break;
            }
        }

        return matchedTarget
            ?? throw new InvalidOperationException(
                $"OCR target '{targetText}' was found in a text line, " +
                "but its word-level geometry could not be resolved.");
    }


    /// <summary>
    /// Creates the union bounds for a contiguous range of OCR words.
    /// </summary>
    /// <param name="words">The OCR words containing the matched range.</param>
    /// <param name="startIndex">The inclusive start index of the range.</param>
    /// <param name="endIndex">The inclusive end index of the range.</param>
    /// <param name="result">
    /// The OCR result used for argument-error attribution.
    /// </param>
    /// <returns>
    /// The smallest rectangle containing all OCR words in the specified range.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the word range contains invalid word data.
    /// </exception>
    private static Rectangle CreateUnionBounds(
        IReadOnlyList<OcrTextWord> words,
        int startIndex,
        int endIndex,
        OcrResult result)
    {
        OcrTextWord firstWord =
            words[startIndex]
            ?? throw new ArgumentException(
                "The matched OCR line contains a null word.",
                nameof(result));

        double left =
            firstWord.Bounds.TopLeft.X;

        double top =
            firstWord.Bounds.TopLeft.Y;

        double right =
            firstWord.Bounds.BottomRight.X;

        double bottom =
            firstWord.Bounds.BottomRight.Y;

        for (int index = startIndex + 1;
             index <= endIndex;
             index++)
        {
            OcrTextWord word =
                words[index]
                ?? throw new ArgumentException(
                    "The matched OCR line contains a null word.",
                    nameof(result));

            Rectangle bounds =
                word.Bounds;

            left =
                Math.Min(
                    left,
                    bounds.TopLeft.X);

            top =
                Math.Min(
                    top,
                    bounds.TopLeft.Y);

            right =
                Math.Max(
                    right,
                    bounds.BottomRight.X);

            bottom =
                Math.Max(
                    bottom,
                    bounds.BottomRight.Y);
        }

        return Rectangle.FromXYWH(
            left,
            top,
            right - left,
            bottom - top);
    }


    /// <summary>
    /// Removes whitespace from OCR text while preserving all other characters
    /// and their ordinal ordering.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>The text with whitespace removed.</returns>
    private static string NormalizeWithoutWhitespace(
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ReadOnlySpan<char> source = value.AsSpan();

        int nonWhitespaceCount = 0;

        foreach (char character in source)
        {
            if (!char.IsWhiteSpace(character))
            {
                nonWhitespaceCount++;
            }
        }

        if (nonWhitespaceCount == source.Length)
        {
            return value;
        }

        return string.Create(
            nonWhitespaceCount,
            value,
            static (destination, state) =>
            {
                ReadOnlySpan<char> sourceSpan =
                    state.AsSpan();

                int destinationIndex = 0;

                foreach (char character in sourceSpan)
                {
                    if (char.IsWhiteSpace(character))
                    {
                        continue;
                    }

                    destination[destinationIndex++] =
                        character;
                }
            });
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