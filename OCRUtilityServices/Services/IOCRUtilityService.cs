using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Provides optical character recognition for encoded images.
/// </summary>
public interface IOCRUtilityService
{
    /// <summary>
    /// Recognizes text from an encoded image.
    /// </summary>
    Task<string> QuickOcrAsync(byte[] imageBuffer);

    /// <summary>
    /// Recognizes text and text-line locations from an encoded image.
    /// </summary>
    Task<OcrResult> RecognizeAsync(
        byte[] imageBuffer,
        CancellationToken cancellationToken = default);
}