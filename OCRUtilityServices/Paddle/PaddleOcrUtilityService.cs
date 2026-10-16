using CoordinateUtilityServices;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OCRUtilityServices.Paddle;

/// <summary>
/// Implements the existing OCR contract using an injected PaddleOCR ONNX backend.
/// </summary>
public sealed class PaddleOcrUtilityService : IOCRUtilityService
{
    private readonly PaddleOcrOnnxBackend _backend;

    /// <summary>Creates the provider with an existing inference backend.</summary>
    public PaddleOcrUtilityService(PaddleOcrOnnxBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc/>
    public async Task<string> QuickOcrAsync(byte[] imageBuffer)
        => (await RecognizeAsync(imageBuffer).ConfigureAwait(false)).Text;

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, CancellationToken cancellationToken = default)
        => RecognizeAsync(imageBuffer, "zh-tw", cancellationToken);

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, string languageTag, CancellationToken cancellationToken = default)
        => RecognizeCoreAsync(imageBuffer, null, languageTag, cancellationToken);

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, OcrRecognitionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RecognizeCoreAsync(imageBuffer, options.Region, options.LanguageTag ?? "zh-tw", cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeRegionAsync(byte[] imageBuffer, CoordinateUtilityServices.Rectangle region, string languageTag, CancellationToken cancellationToken = default)
        => RecognizeCoreAsync(imageBuffer, region, languageTag, cancellationToken);

    /// <summary>Decodes, optionally crops, recognizes, and maps geometry back to the source image.</summary>
    private async Task<OcrResult> RecognizeCoreAsync(
        byte[] imageBuffer,
        CoordinateUtilityServices.Rectangle? region,
        string languageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageBuffer);
        if (imageBuffer.Length == 0)
            throw new ArgumentException("The image buffer must not be empty.", nameof(imageBuffer));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        if (!languageTag.Equals("zh-tw", StringComparison.OrdinalIgnoreCase) &&
            !languageTag.Equals("zh-hant", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"The configured PaddleOCR model does not declare support for '{languageTag}'.");
        cancellationToken.ThrowIfCancellationRequested();

        using var imageStream = new MemoryStream(
            imageBuffer,
            writable: false);

        using Image<Rgb24> source =
            await Image.LoadAsync<Rgb24>(
                imageStream,
                cancellationToken).ConfigureAwait(false);
        
        cancellationToken.ThrowIfCancellationRequested();
        int offsetX = 0, offsetY = 0;
        Image<Rgb24> input = source;
        if (region is { } roi)
        {
            double x = roi.TopLeft.X, y = roi.TopLeft.Y, width = roi.Width, height = roi.Height;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
                x < 0 || y < 0 || width <= 0 || height <= 0 ||
                x + width > source.Width || y + height > source.Height ||
                x != Math.Truncate(x) || y != Math.Truncate(y) ||
                width != Math.Truncate(width) || height != Math.Truncate(height))
                throw new ArgumentOutOfRangeException(nameof(region), "OCR region must be an integer rectangle inside the source image.");
            offsetX = checked((int)x);
            offsetY = checked((int)y);
            input = source.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(offsetX, offsetY, checked((int)width), checked((int)height))));
        }
        try
        {
            var detected = _backend.Recognize(input, cancellationToken);
            var lines = new List<OcrTextLine>(detected.Count);
            foreach (var line in detected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.Width <= 0 || line.Height <= 0 ||
                    line.X < 0 || line.Y < 0 ||
                    (long)line.X + line.Width > input.Width ||
                    (long)line.Y + line.Height > input.Height)
                    throw new InvalidDataException("PaddleOCR returned invalid line geometry.");
                lines.Add(new OcrTextLine(line.Text, CoordinateUtilityServices.Rectangle.FromXYWH(
                    line.X + offsetX, line.Y + offsetY, line.Width, line.Height)));
                // Do not invent word-level geometry for a line-level recognition model.
            }
            return new OcrResult(string.Join(Environment.NewLine, lines.Select(line => line.Text)), lines);
        }
        finally
        {
            if (!ReferenceEquals(input, source)) input.Dispose();
        }
    }
}
