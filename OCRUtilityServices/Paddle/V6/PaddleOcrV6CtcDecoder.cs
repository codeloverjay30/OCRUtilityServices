using System.Text.Json;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Decodes PaddleOCR v6 CTC probabilities using the original ordered character dictionary.
/// </summary>
public sealed class PaddleOcrV6CtcDecoder: IPaddleOcrV6CtcDecoder
{
    private readonly string[] _vocabulary;

    /// <summary>Creates a decoder from the exported JSON dictionary.</summary>
    public PaddleOcrV6CtcDecoder(string dictionaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryPath);
        string[] characters = JsonSerializer.Deserialize<string[]>(File.ReadAllText(dictionaryPath))
            ?? throw new InvalidDataException("OCR dictionary is null.");
        if (characters.Length != 18708 || characters.Any(x => x is null))
            throw new InvalidDataException("OCR dictionary must contain 18708 string entries.");
        _vocabulary = new string[18710];
        _vocabulary[0] = string.Empty;
        Array.Copy(characters, 0, _vocabulary, 1, characters.Length);
        _vocabulary[^1] = " ";
    }

    /// <summary>Decodes one batch of CTC probabilities and returns text and mean confidence.</summary>
    public (string Text, float Score) Decode(ReadOnlySpan<float> probabilities, int steps, int classes)
    {
        if (steps <= 0 || classes != _vocabulary.Length ||
            probabilities.Length != checked(steps * classes))
            throw new InvalidDataException("Unexpected OCR output tensor shape.");

        var text = new System.Text.StringBuilder();
        int previous = -1;
        double total = 0;
        int count = 0;
        for (int step = 0; step < steps; step++)
        {
            ReadOnlySpan<float> row = probabilities.Slice(step * classes, classes);
            int best = 0;
            float score = float.NegativeInfinity;
            for (int c = 0; c < row.Length; c++)
            {
                if (!float.IsFinite(row[c]))
                    throw new InvalidDataException("OCR output contains non-finite values.");
                if (row[c] > score) { score = row[c]; best = c; }
            }
            if (best != 0 && best != previous)
            {
                text.Append(_vocabulary[best]);
                total += score;
                count++;
            }
            previous = best;
        }
        return (text.ToString(), count == 0 ? 0 : (float)(total / count));
    }
}
