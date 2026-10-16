using System.IO.Abstractions;
using System.Text.Json;
using OCRUtilityServices.AnnotationTool.Models;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;

namespace OCRUtilityServices.AnnotationTool.Services;

/// <summary>
/// Generates OCR annotation drafts without modifying the original screenshots.
/// </summary>
public sealed class PaddleOcrAnnotationGenerator
{
    private readonly IFileSystem _fileSystem;
    private readonly IOCRUtilityService _ocr;
    private readonly IOcrTextMatcher _matcher;

    /// <summary>
    /// Initializes the annotation generator.
    /// </summary>
    public PaddleOcrAnnotationGenerator(
        IFileSystem fileSystem,
        IOCRUtilityService ocr,
        IOcrTextMatcher matcher)
    {
        _fileSystem = fileSystem
            ?? throw new ArgumentNullException(nameof(fileSystem));
        _ocr = ocr
            ?? throw new ArgumentNullException(nameof(ocr));
        _matcher = matcher
            ?? throw new ArgumentNullException(nameof(matcher));
    }

    /// <summary>
    /// Recognizes a screenshot and writes an unverified annotation draft.
    /// </summary>
    public async Task GenerateAsync(
        string imagePath,
        string outputPath,
        string targetText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetText);

        cancellationToken.ThrowIfCancellationRequested();

        byte[] imageBytes = await _fileSystem.File
            .ReadAllBytesAsync(imagePath, cancellationToken);

        OcrResult result = await _ocr.RecognizeAsync(
            imageBytes,
            "zh-TW",
            cancellationToken);

        OcrTextMatch match = _matcher.FindUniqueMatch(
            result,
            targetText,
            OcrTextMatchMode.NormalizedContains);

        var bounds = match.Bounds;

        var draft = new AnnotationDraft(
            Fixture: _fileSystem.Path.GetFileName(imagePath),
            TargetText: targetText,
            Status: "PendingReview",
            TextBounds: new AnnotationBounds(
                bounds.TopLeft.X,
                bounds.TopLeft.Y,
                bounds.Width,
                bounds.Height),
            ButtonBounds: null);

        string json = JsonSerializer.Serialize(
            draft,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        string? directory = _fileSystem.Path
            .GetDirectoryName(outputPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Write a temporary file first, so an interrupted write does not
        // leave a partially written final JSON document.
        string temporaryPath = outputPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await _fileSystem.File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken);

            // Never overwrite a previously reviewed annotation.
            _fileSystem.File.Move(
                temporaryPath,
                outputPath);
        }
        finally
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }
        }
    }
}
