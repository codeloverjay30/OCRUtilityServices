using System.IO.Abstractions;
using CoordinateUtilityServices;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = CoordinateUtilityServices.Rectangle;

namespace OCRUtilityServices.Paddle.Python;

/// <summary>Provides opt-in Python OCR without granting click authorization.</summary>
public sealed class PythonOcrUtilityService : IOCRUtilityService
{
    private readonly IFileSystem _fileSystem;
    private readonly IPythonOcrProcessRunner _runner;

    /// <summary>Initializes an OCR adapter with explicit filesystem and process dependencies.</summary>
    public PythonOcrUtilityService(IFileSystem fileSystem, IPythonOcrProcessRunner runner)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc/>
    public async Task<string> QuickOcrAsync(byte[] imageBuffer) =>
        (await RecognizeAsync(imageBuffer).ConfigureAwait(false)).Text;

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, CancellationToken cancellationToken = default) =>
        RecognizeAsync(imageBuffer, "zh-TW", cancellationToken);

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, string languageTag, CancellationToken cancellationToken = default) =>
        RecognizeCoreAsync(imageBuffer, languageTag, cancellationToken);

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeAsync(byte[] imageBuffer, OcrRecognitionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Region is not null)
            throw new NotSupportedException("Python OCR regional recognition is not implemented.");
        return RecognizeCoreAsync(imageBuffer, options.LanguageTag ?? "zh-TW", cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OcrResult> RecognizeRegionAsync(byte[] imageBuffer, Rectangle region, string languageTag, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Python OCR regional recognition is not implemented.");

    /// <summary>Validates the source image and deletes the temporary input after OCR.</summary>
    private async Task<OcrResult> RecognizeCoreAsync(byte[] imageBuffer, string languageTag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageBuffer);
        if (imageBuffer.Length == 0)
            throw new ArgumentException("The image buffer must not be empty.", nameof(imageBuffer));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        if (!languageTag.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) &&
            !languageTag.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Python OCR supports Traditional Chinese only.");
        cancellationToken.ThrowIfCancellationRequested();
        using Image<Rgb24> image = Image.Load<Rgb24>(imageBuffer);
        string tempPath = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(),
            "issue59-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await _fileSystem.File.WriteAllBytesAsync(tempPath, imageBuffer, cancellationToken).ConfigureAwait(false);
            string json = await _runner.RunAsync(tempPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return PythonOcrContractAdapter.Convert(json, image.Width, image.Height);
        }
        finally
        {
            if (_fileSystem.File.Exists(tempPath))
                _fileSystem.File.Delete(tempPath);
        }
    }
}
