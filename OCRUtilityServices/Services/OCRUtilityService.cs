using OCRUtilityServices.Models;
using OcrResult = OCRUtilityServices.Models.OcrResult;


#if WINDOWS
using Windows.Globalization;
using CoordinateUtilityServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
#endif

namespace OCRUtilityServices.Services;

/// <summary>
/// Provides optical character recognition using the Windows OCR engine.
/// </summary>
public sealed class OCRUtilityService : IOCRUtilityService
{
    /// <summary>
    /// Recognizes text from an encoded image.
    /// </summary>
    public async Task<string> QuickOcrAsync(byte[] imageBuffer)
    {
        OcrResult result = await RecognizeAsync(imageBuffer)
            .ConfigureAwait(false);

        return result.Text;
    }

    public async Task<OcrResult> RecognizeAsync(
        byte[] imageBuffer,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(
            imageBuffer,
            "zh-tw",
            cancellationToken)
            .ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public async Task<OcrResult> RecognizeAsync(
        byte[] imageBuffer,
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBuffer);

        if (imageBuffer.Length == 0)
        {
            throw new ArgumentException(
                "The image buffer must not be empty.",
                nameof(imageBuffer));
        }

        cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
        using var stream = new InMemoryRandomAccessStream();

        await stream.WriteAsync(imageBuffer.AsBuffer());

        cancellationToken.ThrowIfCancellationRequested();

        stream.Seek(0);

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(stream);

        using SoftwareBitmap bitmap =
            await decoder.GetSoftwareBitmapAsync();

        cancellationToken.ThrowIfCancellationRequested();

        Language language = new(languageTag);

        OcrEngine engine =
            OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException(
                $"Windows OCR engine initialization failed for language '{language.LanguageTag}'.");

        var recognized = await engine.RecognizeAsync(bitmap);

        cancellationToken.ThrowIfCancellationRequested();

        var lines = new List<OcrTextLine>(
            recognized.Lines.Count);

        foreach (var line in recognized.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            double left = double.PositiveInfinity;
            double top = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            double bottom = double.NegativeInfinity;

            foreach (var word in line.Words)
            {
                var bounds = word.BoundingRect;

                left = Math.Min(left, bounds.X);
                top = Math.Min(top, bounds.Y);
                right = Math.Max(
                    right,
                    bounds.X + bounds.Width);
                bottom = Math.Max(
                    bottom,
                    bounds.Y + bounds.Height);
            }

            Rectangle rectangle = Rectangle.FromXYWH(
                left,
                top,
                right - left,
                bottom - top);

            lines.Add(new OcrTextLine(
                line.Text,
                rectangle));
        }

        return new OcrResult(
            recognized.Text,
            lines);
#else
        throw new PlatformNotSupportedException(
            "This OCR implementation requires the Windows target framework.");
#endif
    }
}