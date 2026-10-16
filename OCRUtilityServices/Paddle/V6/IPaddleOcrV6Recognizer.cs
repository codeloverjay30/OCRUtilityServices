using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Defines text recognition operations for cropped images
/// using the PaddleOCR v6 recognition model.
/// </summary>
public interface IPaddleOcrV6Recognizer : IDisposable
{
    /// <summary>
    /// Recognizes text from a pre-cropped RGB image.
    /// </summary>
    /// <param name="image">The cropped text image.</param>
    /// <param name="cancellationToken">
    /// A token used to request cancellation.
    /// </param>
    /// <returns>
    /// The recognized text and its confidence score.
    /// </returns>
    (string Text, float Score) Recognize(
        Image<Rgb24> image,
        CancellationToken cancellationToken = default);
}