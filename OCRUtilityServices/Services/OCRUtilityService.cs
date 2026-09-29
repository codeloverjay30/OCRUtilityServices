using OCRUtilityServices.Models;
using OcrResult = OCRUtilityServices.Models.OcrResult;
using OCRUtilityServices.Internal;
using CoordinateUtilityServices;

#if WINDOWS
using Windows.Globalization;
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

        return await RecognizeSoftwareBitmapAsync(
            bitmap,
            languageTag,
            sourceRegion: null,
            cancellationToken)
        .ConfigureAwait(false);
#else
        throw new PlatformNotSupportedException(
            "This OCR implementation requires the Windows target framework.");
#endif
    }

    /// <inheritdoc/>
    public async Task<OcrResult> RecognizeRegionAsync(
        byte[] imageBuffer,
        Rectangle region,
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

        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
        using var stream = new InMemoryRandomAccessStream();

        await stream.WriteAsync(
            imageBuffer.AsBuffer());

        cancellationToken.ThrowIfCancellationRequested();

        stream.Seek(0);

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(stream);

        OcrRegionGeometry.ValidateWithinImage(
            region,
            decoder.PixelWidth,
            decoder.PixelHeight);

        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = checked((uint)region.TopLeft.X),
                Y = checked((uint)region.TopLeft.Y),
                Width = checked((uint)region.Width),
                Height = checked((uint)region.Height)
            }
        };

        using SoftwareBitmap bitmap =
            await decoder.GetSoftwareBitmapAsync(
                decoder.BitmapPixelFormat,
                decoder.BitmapAlphaMode,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

        System.Diagnostics.Debug.WriteLine(
            $"OCR ROI bitmap: {bitmap.PixelWidth}x{bitmap.PixelHeight}; " +
            $"Source region: X={region.TopLeft.X}, Y={region.TopLeft.Y}, " +
            $"Width={region.Width}, Height={region.Height}");

        cancellationToken.ThrowIfCancellationRequested();

        return await RecognizeSoftwareBitmapAsync(
                bitmap,
                languageTag,
                region,
                cancellationToken)
            .ConfigureAwait(false);
#else
    throw new PlatformNotSupportedException(
        "This OCR implementation requires the Windows target framework.");
#endif
    }

#if WINDOWS

    /// <summary>
    /// Recognizes text from a decoded software bitmap and maps the recognized
    /// geometry to source-image coordinates when a source region is specified.
    /// </summary>
    /// <param name="bitmap">The decoded bitmap to recognize.</param>
    /// <param name="languageTag">The BCP-47 language tag used for recognition.</param>
    /// <param name="sourceRegion">
    /// The source-image region represented by the bitmap, or <see langword="null"/>
    /// when the bitmap represents the complete source image.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the recognition operation.
    /// </param>
    /// <returns>
    /// The recognized text, lines, words, and source-image geometry.
    /// </returns>
    private static async Task<OcrResult> RecognizeSoftwareBitmapAsync(
        SoftwareBitmap bitmap,
        string languageTag,
        Rectangle? sourceRegion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        cancellationToken.ThrowIfCancellationRequested();

        Language language = new(languageTag);

        OcrEngine engine =
            OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException(
                $"Windows OCR engine initialization failed for language '{language.LanguageTag}'.");

        var recognized =
            await engine.RecognizeAsync(bitmap);

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

            var words = new List<OcrTextWord>(
                line.Words.Count);

            foreach (var word in line.Words)
            {
                var bounds = word.BoundingRect;

                Rectangle wordBounds =
                    Rectangle.FromXYWH(
                        bounds.X,
                        bounds.Y,
                        bounds.Width,
                        bounds.Height);

                if (sourceRegion is Rectangle region)
                {
                    wordBounds =
                        OcrRegionGeometry.TranslateToSourceCoordinates(
                            wordBounds,
                            region);
                }

                words.Add(
                    new OcrTextWord(
                        word.Text,
                        wordBounds));

                left = Math.Min(
                    left,
                    wordBounds.TopLeft.X);

                top = Math.Min(
                    top,
                    wordBounds.TopLeft.Y);

                right = Math.Max(
                    right,
                    wordBounds.BottomRight.X);

                bottom = Math.Max(
                    bottom,
                    wordBounds.BottomRight.Y);
            }

            Rectangle lineBounds =
                Rectangle.FromXYWH(
                    left,
                    top,
                    right - left,
                    bottom - top);

            lines.Add(
                new OcrTextLine(
                    line.Text,
                    lineBounds,
                    words));
        }

        return new OcrResult(
            recognized.Text,
            lines);
    }

#endif
}