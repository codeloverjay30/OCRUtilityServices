using CoordinateUtilityServices;
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

    /// <summary>
    /// Recognizes text from encoded image data by using the specified OCR language.
    /// </summary>
    /// <param name="imageBuffer">
    /// The encoded image data to recognize.
    /// </param>
    /// <param name="languageTag">
    /// The BCP-47 language tag used to select the OCR recognizer.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the recognition operation.
    /// </param>
    /// <returns>
    /// The recognized text and text-line bounds.
    /// </returns>
    Task<OcrResult> RecognizeAsync(
        byte[] imageBuffer,
        string languageTag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recognizes text within a specified region of an encoded image
    /// by using the specified OCR language.
    /// </summary>
    /// <param name="imageBuffer">
    /// The encoded source image data to recognize.
    /// </param>
    /// <param name="region">
    /// The region to recognize, expressed in source-image pixel coordinates.
    /// </param>
    /// <param name="languageTag">
    /// The BCP-47 language tag used to select the OCR recognizer.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the recognition operation.
    /// </param>
    /// <returns>
    /// The recognized text with line and word bounds expressed in
    /// source-image coordinates.
    /// </returns>
    Task<OcrResult> RecognizeRegionAsync(
        byte[] imageBuffer,
        Rectangle region,
        string languageTag,
        CancellationToken cancellationToken = default);
    
}