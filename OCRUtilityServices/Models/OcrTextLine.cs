using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Represents a recognized text line, its bounds, and its recognized words
/// in the source image.
/// </summary>
public sealed record OcrTextLine
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OcrTextLine"/> class
    /// without word-level OCR information.
    /// </summary>
    /// <param name="text">
    /// The recognized text of the line.
    /// </param>
    /// <param name="bounds">
    /// The bounds of the complete recognized line in the source image.
    /// </param>
    public OcrTextLine(
        string text,
        Rectangle bounds)
        : this(
            text,
            bounds,
            Array.Empty<OcrTextWord>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OcrTextLine"/> class
    /// with word-level OCR information.
    /// </summary>
    /// <param name="text">
    /// The recognized text of the line.
    /// </param>
    /// <param name="bounds">
    /// The bounds of the complete recognized line in the source image.
    /// </param>
    /// <param name="words">
    /// The recognized words and their bounds in the source image.
    /// </param>
    public OcrTextLine(
        string text,
        Rectangle bounds,
        IReadOnlyList<OcrTextWord> words)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);

        Text = text;
        Bounds = bounds;
        Words = words;
    }

    /// <summary>
    /// Gets the recognized text of the line.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the bounds of the complete recognized line in the source image.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the recognized words and their bounds in the source image.
    /// </summary>
    public IReadOnlyList<OcrTextWord> Words { get; }
}