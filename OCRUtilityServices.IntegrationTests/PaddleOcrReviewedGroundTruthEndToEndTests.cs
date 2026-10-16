using System.IO.Abstractions;
using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle;
using OCRUtilityServices.Paddle.V6;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Xunit.Abstractions;

namespace OCRUtilityServices.IntegrationTests;

/// <summary>
/// Verifies real screenshot localization against independently approved,
/// digest-pinned ground truth without performing device clicks.
/// </summary>
public sealed class PaddleOcrReviewedGroundTruthEndToEndTests
{
    private const string TargetText = "登入遊戲";
    private const int CropPadding = 8;
    private const float MinimumConfidence = 0.99f;
    private readonly IFileSystem _files = new FileSystem();
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes the screenshot acceptance suite.
    /// </summary>
    public PaddleOcrReviewedGroundTruthEndToEndTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Theory]
    [InlineData("ocr-diagnostic-000003.png")]
    [InlineData("ocr-diagnostic-000004.png")]
    [InlineData("ocr-diagnostic-000005.png")]
    [InlineData("ocr-diagnostic-000006.png")]
    [InlineData("ocr-diagnostic-000007.png")]
    [InlineData("ocr-diagnostic-000008.png")]
    [InlineData("ocr-diagnostic-000009.png")]
    [InlineData("ocr-diagnostic-000010.png")]
    [InlineData("ocr-diagnostic-000011.png")]
    [InlineData("ocr-diagnostic-000012.png")]
    [Trait("Category", "PaddleOcrReviewedGroundTruthE2E")]
    public async Task RealScreenshot_ReviewedGroundTruth_ShouldAuthorizeSafePoint(
        string fixtureName)
    {
        string fixtureDirectory = RequireDirectory("PADDLEOCR_FIXTURE_DIR");
        string modelDirectory = RequireDirectory("PADDLEOCR_V6_MODEL_DIR");
        string manifestPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_APPROVED_GROUND_TRUTH_MANIFEST")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_APPROVED_GROUND_TRUTH_MANIFEST is required."));

        byte[] manifestBytes = await _files.File.ReadAllBytesAsync(manifestPath);
        ApprovedFixture[] approved;
        try
        {
            approved = JsonSerializer.Deserialize<ApprovedFixture[]>(manifestBytes)
                ?? throw new InvalidDataException("Approved manifest is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Approved manifest JSON is invalid.", ex);
        }

        // Never choose an arbitrary entry when the manifest is ambiguous.
        ApprovedFixture[] entries = approved
            .Where(item => string.Equals(item.Fixture, fixtureName, StringComparison.Ordinal))
            .ToArray();
        entries.Should().ContainSingle(
            "every real screenshot must have exactly one independently approved manifest entry");

        ApprovedFixture entry = entries.Single();
        entry.AnnotationPath.Should().NotBeNullOrWhiteSpace();
        entry.AnnotationSha256.Should().NotBeNullOrWhiteSpace();
        entry.ScreenshotSha256.Should().NotBeNullOrWhiteSpace();

        string screenshotPath = RequireFile(_files.Path.Combine(fixtureDirectory, fixtureName));
        string annotationPath = RequireFile(entry.AnnotationPath!);

        IGroundTruthLoader loader = new GroundTruthLoader(_files);
        ReviewedGroundTruth truth = await loader.LoadAsync(
            annotationPath,
            screenshotPath,
            entry.AnnotationSha256!,
            entry.ScreenshotSha256!);

        truth.TargetText.Should().Be(TargetText);

        byte[] screenshotBytes = await _files.File.ReadAllBytesAsync(screenshotPath);
        using Image<Rgb24> screenshot = Image.Load<Rgb24>(screenshotBytes);
        screenshot.Width.Should().Be(truth.ImageWidth);
        screenshot.Height.Should().Be(truth.ImageHeight);

        var options = new PaddleOcrOptions
        {
            DetectionModelPath = RequireEnvironmentFile("PADDLEOCR_DETECTION_MODEL"),
            RecognitionModelPath = RequireEnvironmentFile("PADDLEOCR_RECOGNITION_MODEL"),
            DictionaryPath = RequireEnvironmentFile("PADDLEOCR_DICTIONARY")
        };

        using var backend = new PaddleOcrOnnxBackend(options);
        IOCRUtilityService detector = new PaddleOcrUtilityService(backend);
        OcrResult detections = await detector.RecognizeAsync(screenshotBytes, "zh-TW");

        string modelPath = RequireFile(_files.Path.Combine(modelDirectory, "inference.onnx"));
        string dictionaryPath = RequireFile(
            _files.Path.Combine(modelDirectory, "character_dict.json"));

        var preprocessor = new PaddleOcrV6ImagePreprocessor();
        using var inference = new PaddleOcrV6OnnxInferenceEngine(modelPath);
        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);
        using var recognizer = new PaddleOcrV6Recognizer(preprocessor, inference, decoder);

        var matches = new List<(OcrTextLine Line, string Text, float Score)>();
        foreach (OcrTextLine detectedLine in detections.Lines)
        {
            double leftBound = detectedLine.Bounds.TopLeft.X;
            double topBound = detectedLine.Bounds.TopLeft.Y;
            double rightBound = detectedLine.Bounds.BottomRight.X;
            double bottomBound = detectedLine.Bounds.BottomRight.Y;

            bool valid = double.IsFinite(leftBound) && double.IsFinite(topBound)
                && double.IsFinite(rightBound) && double.IsFinite(bottomBound)
                && leftBound >= 0 && topBound >= 0
                && rightBound > leftBound && bottomBound > topBound
                && rightBound <= screenshot.Width && bottomBound <= screenshot.Height;
            valid.Should().BeTrue("every V3 candidate must have valid source-image geometry");

            int left = Math.Max(0, checked((int)Math.Floor(leftBound)) - CropPadding);
            int top = Math.Max(0, checked((int)Math.Floor(topBound)) - CropPadding);
            int right = Math.Min(screenshot.Width,
                checked((int)Math.Ceiling(rightBound)) + CropPadding);
            int bottom = Math.Min(screenshot.Height,
                checked((int)Math.Ceiling(bottomBound)) + CropPadding);

            using Image<Rgb24> crop = screenshot.Clone(context => context.Crop(
                new SixLabors.ImageSharp.Rectangle(
                    left, top, checked(right - left), checked(bottom - top))));

            (string recognizedText, float recognitionScore) = recognizer.Recognize(crop);
            float.IsFinite(recognitionScore).Should().BeTrue(
                "non-finite OCR confidence must never authorize a click");

            // A duplicate matching label must not be hidden by a low confidence score.
            if (string.Equals(recognizedText, TargetText, StringComparison.Ordinal))
            {
                matches.Add((detectedLine, recognizedText, recognitionScore));
            }
        }

        matches.Should().ContainSingle(
            "a real screenshot must have exactly one V6-recognized login label");

        (OcrTextLine line, string text, float score) = matches.Single();
        score.Should().BeGreaterThan(MinimumConfidence);

        double detectedLeft = line.Bounds.TopLeft.X;
        double detectedTop = line.Bounds.TopLeft.Y;
        double detectedRight = line.Bounds.BottomRight.X;
        double detectedBottom = line.Bounds.BottomRight.Y;

        double truthLeft = truth.TextBounds.TopLeft.X;
        double truthTop = truth.TextBounds.TopLeft.Y;
        double truthRight = truth.TextBounds.BottomRight.X;
        double truthBottom = truth.TextBounds.BottomRight.Y;

        double intersectionWidth = Math.Max(
            0, Math.Min(detectedRight, truthRight) - Math.Max(detectedLeft, truthLeft));

        double intersectionHeight = Math.Max(
            0, Math.Min(detectedBottom, truthBottom) - Math.Max(detectedTop, truthTop));

        double intersection = intersectionWidth * intersectionHeight;

        double detectedArea = line.Bounds.Width * line.Bounds.Height;
        double truthArea = truth.TextBounds.Width * truth.TextBounds.Height;
        double union = detectedArea + truthArea - intersection;

        // Calculate IOU
        double iou = union > 0 ? intersection / union : 0;

        // Output info about IOU
        _output.WriteLine(
            $"Fixture={fixtureName}; " +
            $"Detected=({detectedLeft:G9},{detectedTop:G9}," +
            $"{line.Bounds.Width:G9},{line.Bounds.Height:G9}); " +
            $"GroundTruth=({truthLeft:G9},{truthTop:G9}," +
            $"{truth.TextBounds.Width:G9},{truth.TextBounds.Height:G9}); " +
            $"IoU={iou:G9}; Score={score:G9}");
    
        IGroundedClickAuthorizationService authorization =
            new GroundedClickAuthorizationService(
                new GroundedClickValidator(
                    new ReviewedPaddedTextGeometryPolicy()));

        (double X, double Y) point = authorization.Authorize(
            text, score, line.Bounds, truth);

        double.IsFinite(point.X).Should().BeTrue();
        double.IsFinite(point.Y).Should().BeTrue();
        point.X.Should().BeInRange(0, screenshot.Width - 1);
        point.Y.Should().BeInRange(0, screenshot.Height - 1);

        _output.WriteLine(
            $"ClickSafety=VERIFIED; Fixture={fixtureName}; " +
            $"Score={score:G9}; Point=({point.X:G9},{point.Y:G9}); " +
            "ClickExecuted=False");
    }

    /// <summary>
    /// Gets a required existing directory.
    /// </summary>
    private string RequireDirectory(string name)
    {
        string? path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path) || !_files.Directory.Exists(path))
            throw new InvalidOperationException($"{name} must reference an existing directory.");
        return path;
    }

    /// <summary>
    /// Gets a required existing model file from an environment variable.
    /// </summary>
    private string RequireEnvironmentFile(string name)
    {
        string? path = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{name} is required.");
        return RequireFile(path);
    }

    /// <summary>
    /// Ensures a configured file exists.
    /// </summary>
    private string RequireFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_files.File.Exists(path))
            throw new FileNotFoundException($"Required test asset is missing: {path}", path);
        return path;
    }

    /// <summary>
    /// Represents a trusted manifest entry supplied independently of OCR.
    /// </summary>
    private sealed record ApprovedFixture(
        string? Fixture,
        string? AnnotationPath,
        string? AnnotationSha256,
        string? ScreenshotSha256);
}
