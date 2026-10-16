using System.Diagnostics;
using System.IO.Abstractions;
using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle;
using OCRUtilityServices.Services;
using Xunit;
using Xunit.Abstractions;

namespace OCRUtilityServices.IntegrationTests;

/// <summary>
/// Exercises the real PaddleOCR ONNX detection and recognition pipeline using local model assets.
/// Run explicitly with: dotnet test --filter "Category=PaddleOcrIntegration".
/// </summary>
public sealed class PaddleOcrIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IFileSystem _fileSystem = new FileSystem();

    /// <summary>
    /// Initializes the PaddleOCR integration test suite.
    /// </summary>
    public PaddleOcrIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("ocr-diagnostic-000007.png")]
    [InlineData("ocr-diagnostic-000008.png")]
    [InlineData("ocr-diagnostic-000009.png")]
    [InlineData("ocr-diagnostic-000010.png")]
    [InlineData("ocr-diagnostic-000011.png")]
    [InlineData("ocr-diagnostic-000012.png")]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task RecognizeAsync_LoginScreenshot_ShouldLocateSingleTargetWithValidBounds(
        string fixtureName)
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);
        byte[] screenshot = await ReadFixtureAsync(fixtureName);
        var stopwatch = Stopwatch.StartNew();

        OcrResult result = await sut.RecognizeAsync(screenshot, "zh-TW");

        stopwatch.Stop();
        WriteDiagnostics(fixtureName, result, stopwatch.Elapsed);
        var candidates = result.Lines
            .Where(line => Normalize(line.Text).Contains("登入遊戲", StringComparison.Ordinal))
            .ToArray();

        candidates.Should().ContainSingle(
            "the visible login button must be uniquely reacquired on every diagnostic screenshot");

        OcrTextLine target = candidates.Single();
        target.Bounds.Width.Should().BeGreaterThan(0);
        target.Bounds.Height.Should().BeGreaterThan(0);
        target.Bounds.TopLeft.X.Should().BeGreaterThanOrEqualTo(0);
        target.Bounds.TopLeft.Y.Should().BeGreaterThanOrEqualTo(0);
        target.Bounds.BottomRight.X.Should().BeLessThanOrEqualTo(2340);
        target.Bounds.BottomRight.Y.Should().BeLessThanOrEqualTo(1080);
        // These are screenshot dimensions, not a fabricated click coordinate.
        // A separate annotated ground-truth fixture is needed to assert button containment.
    }

    [Fact]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task RecognizeRegionAsync_LoginRegion_ShouldReturnSourceImageCoordinates()
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);
        byte[] screenshot = await ReadFixtureAsync("ocr-diagnostic-000007.png");
        // Deliberately use a generous ROI; this is a search region, NOT a click target.
        var region = Rectangle.FromXYWH(600, 400, 1150, 550);

        OcrResult result = await sut.RecognizeRegionAsync(screenshot, region, "zh-TW");

        WriteDiagnostics("login-roi", result, TimeSpan.Zero);
        var candidates = result.Lines
            .Where(line => Normalize(line.Text).Contains("登入遊戲", StringComparison.Ordinal))
            .ToArray();
        candidates.Should().ContainSingle();
        var bounds = candidates.Single().Bounds;
        bounds.TopLeft.X.Should().BeGreaterThanOrEqualTo(region.TopLeft.X);
        bounds.TopLeft.Y.Should().BeGreaterThanOrEqualTo(region.TopLeft.Y);
        bounds.BottomRight.X.Should().BeLessThanOrEqualTo(region.BottomRight.X);
        bounds.BottomRight.Y.Should().BeLessThanOrEqualTo(region.BottomRight.Y);
    }

    [Fact]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task RecognizeAsync_InvalidLanguage_ShouldFailWithExpectedMessage()
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);
        byte[] screenshot = await ReadFixtureAsync("ocr-diagnostic-000007.png");

        Func<Task> act = () => sut.RecognizeAsync(screenshot, "en-US");

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not declare support*en-US*");
    }

    [Fact]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task RecognizeAsync_CancelledToken_ShouldThrowCancellation()
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);
        byte[] screenshot = await ReadFixtureAsync("ocr-diagnostic-000007.png");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => sut.RecognizeAsync(screenshot, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task RecognizeAsync_EmptyBuffer_ShouldFailWithExpectedMessage()
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);

        Func<Task> act = () => sut.RecognizeAsync(Array.Empty<byte>());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*image buffer must not be empty*");
    }

    [Fact]
    [Trait("Category", "PaddleOcrIntegration")]
    public async Task FindUniqueMatch_LineOnlyProvider_ExactLine_ShouldUseValidatedBounds()
    {
        using var backend = CreateBackend();
        IOCRUtilityService sut = new PaddleOcrUtilityService(backend);
        byte[] screenshot = await ReadFixtureAsync("ocr-diagnostic-000007.png");
        OcrResult result = await sut.RecognizeAsync(screenshot, "zh-TW");
        var matcher = new OcrTextMatcher();

        OcrTextLine[] candidates = result.Lines
            .Where(line => Normalize(line.Text).Equals("登入遊戲", StringComparison.Ordinal))
            .ToArray();
        candidates.Should().ContainSingle(
            "line-level fallback requires an exact normalized target");
        candidates.Single().Words.Should().BeEmpty(
            "the PaddleOCR provider must not fabricate word-level bounds");

        OcrTextMatch match = matcher.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        match.Bounds.Should().Be(candidates.Single().Bounds);
    }


    [Fact]
    [Trait("Category", "PaddleOcrDiagnostic")]
    public async Task CompareRecognitionPreprocessing_Fixture000007_ShouldReportBothStrategies()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string expectedText = "登入遊戲";
        const int left = 1106;
        const int top = 744;
        const int width = 123;
        const int height = 21;

        byte[] screenshot = await ReadFixtureAsync(fixtureName);
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            screenshot);
        using var backend = CreateBackend();

        image.Width.Should().BeGreaterThanOrEqualTo(left + width);
        image.Height.Should().BeGreaterThanOrEqualTo(top + height);

        PaddleOcrRecognitionComparison comparison =
            backend.CompareRecognitionPreprocessing(
                image, left, top, width, height);

        comparison.InputWidth.Should().Be(320);
        comparison.InputHeight.Should().Be(48);
        comparison.ContentWidth.Should().BeInRange(1, 320);

        _output.WriteLine($"Fixture={fixtureName}");
        _output.WriteLine($"Crop=X={left},Y={top},Width={width},Height={height}");
        _output.WriteLine($"Expected=[{expectedText}]");
        _output.WriteLine($"Stretch=[{comparison.StretchText}]");
        _output.WriteLine($"AspectRatioPadded=[{comparison.AspectRatioPaddedText}]");
        _output.WriteLine(
            $"Padding=Left:0,Right:{comparison.InputWidth - comparison.ContentWidth}");
        _output.WriteLine(
            $"StretchMatches={comparison.StretchText == expectedText}");
        _output.WriteLine(
            $"PaddedMatches={comparison.AspectRatioPaddedText == expectedText}");

        // C: BGR + Stretch
        _output.WriteLine(
            $"BgrStretch=[{comparison.BgrStretchText}]");

        _output.WriteLine(
            $"BgrStretchMatches={comparison.BgrStretchText == expectedText}");
    

        // D: BGR + AspectRatio Padding
        _output.WriteLine(
            $"BgrPadded=[{comparison.BgrAspectRatioPaddedText}]");
    

        _output.WriteLine(
            $"BgrPaddedMatches={comparison.BgrAspectRatioPaddedText == expectedText}");
    
    }

    [Fact]
    [Trait("Category", "PaddleOcrDiagnostic")]
    public async Task RecognizeDiagnosticCropRgbStretch_Fixture000007_ShouldCompareCropMargins()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string expectedText = "登入遊戲";
        const int originalLeft = 1106;
        const int originalTop = 744;
        const int originalWidth = 123;
        const int originalHeight = 21;

        byte[] screenshot = await ReadFixtureAsync(fixtureName);
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            screenshot);
        using var backend = CreateBackend();

        foreach (int margin in new[] { 0, 2, 4, 8 })
        {
            int left = originalLeft - margin;
            int top = originalTop - margin;
            int width = originalWidth + 2 * margin;
            int height = originalHeight + 2 * margin;

            left.Should().BeGreaterThanOrEqualTo(0);
            top.Should().BeGreaterThanOrEqualTo(0);
            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);
            ((long)left + width).Should().BeLessThanOrEqualTo(image.Width);
            ((long)top + height).Should().BeLessThanOrEqualTo(image.Height);

            string recognized = backend.RecognizeDiagnosticCropRgbStretch(
                image, left, top, width, height);

            _output.WriteLine(
                $"Margin={margin}px, Crop=X={left},Y={top},Width={width},Height={height}, " +
                $"Recognized=[{recognized}], Matches={recognized == expectedText}");

            if (margin == 0)
            {
                recognized.Should().Be("登人游虐",
                    "the unexpanded crop must reproduce the previously observed RGB + Stretch baseline");
            }
        }
    }

    [Fact]
    [Trait("Category", "PaddleOcrDiagnostic")]
    public async Task DiagnoseCtcTopK_Fixture000007_Margin2_ShouldReportDictionaryAndCandidates()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string expected = "登入遊戲";
        byte[] screenshot = await ReadFixtureAsync(fixtureName);
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            screenshot);
        using var backend = CreateBackend();

        PaddleOcrCtcDiagnosticResult result = backend.DiagnoseCtcTopK(
            image, 1104, 742, 127, 25, "登入遊戲游虔", topK: 5);

        result.ClassCount.Should().BeGreaterThan(5);
        result.TimeStepCount.Should().BeGreaterThan(0);
        result.TimeSteps.Should().HaveCount(result.TimeStepCount);
        result.TimeSteps.Should().OnlyContain(step => step.Candidates.Count == 5);
        result.DictionaryCoverage.Should().HaveCount(6);

        _output.WriteLine($"Fixture={fixtureName}");
        _output.WriteLine("Crop=X=1104,Y=742,Width=127,Height=25");
        _output.WriteLine($"Expected=[{expected}]");
        _output.WriteLine($"Decoded=[{result.DecodedText}]");
        _output.WriteLine($"ClassCount={result.ClassCount},TimeSteps={result.TimeStepCount}");
        foreach (PaddleOcrCtcDictionaryEntry entry in result.DictionaryCoverage)
        {
            string indices = entry.ClassIndices.Count == 0
                ? "<MISSING>"
                : string.Join(",", entry.ClassIndices);
            _output.WriteLine($"Dictionary[{entry.Character}]=[{indices}]");
        }

        foreach (PaddleOcrCtcTimeStep step in result.TimeSteps)
        {
            string candidates = string.Join(" | ", step.Candidates.Select(
                candidate => $"{candidate.ClassIndex}:{candidate.Label}:{candidate.Score:G9}"));
            _output.WriteLine($"T={step.TimeStep:D3} {candidates}");
        }
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
    [Trait("Category", "PaddleOcrFullScreenLocalization")]
    public async Task RecognizeAsync_FullScreenshot_ShouldLocateUniqueLoginTextAndValidCenter(
        string fixtureName)
    {
        const string targetText = "登入遊戲";
        const int expectedWidth = 2340;
        const int expectedHeight = 1080;

        byte[] screenshot = await ReadFixtureAsync(fixtureName);

        using var backend = CreateBackend();

        IOCRUtilityService service = new PaddleOcrUtilityService(backend);
        IOcrTextMatcher matcher = new OcrTextMatcher();

        OcrResult result = await service.RecognizeAsync(
            screenshot,
            "zh-TW");

        WriteDiagnostics(fixtureName, result, TimeSpan.Zero);

        OcrTextMatch match = matcher.FindUniqueMatch(
            result,
            targetText,
            OcrTextMatchMode.NormalizedContains);

        match.Text.Should().Be(targetText);

        var bounds = match.Bounds;

        double left = bounds.TopLeft.X;
        double top = bounds.TopLeft.Y;
        double right = bounds.BottomRight.X;
        double bottom = bounds.BottomRight.Y;

        double.IsFinite(left).Should().BeTrue();
        double.IsFinite(top).Should().BeTrue();
        double.IsFinite(right).Should().BeTrue();
        double.IsFinite(bottom).Should().BeTrue();

        left.Should().BeGreaterThanOrEqualTo(0);
        top.Should().BeGreaterThanOrEqualTo(0);
        right.Should().BeLessThanOrEqualTo(expectedWidth);
        bottom.Should().BeLessThanOrEqualTo(expectedHeight);

        right.Should().BeGreaterThan(left);
        bottom.Should().BeGreaterThan(top);

        double centerX = left + (right - left) / 2.0;
        double centerY = top + (bottom - top) / 2.0;

        double.IsFinite(centerX).Should().BeTrue();
        double.IsFinite(centerY).Should().BeTrue();

        centerX.Should().BeInRange(left, right);
        centerY.Should().BeInRange(top, bottom);

        centerX.Should().BeInRange(0, expectedWidth - 1);
        centerY.Should().BeInRange(0, expectedHeight - 1);

        _output.WriteLine(
            $"LOCALIZATION: Fixture={fixtureName}, " +
            $"Text=[{match.Text}], " +
            $"Bounds=({left:G9},{top:G9})-({right:G9},{bottom:G9}), " +
            $"TextCenter=({centerX:G9},{centerY:G9})");
    }



    [Fact]
    [Trait("Category", "PaddleOcrFullScreenLocalization")]
    public async Task RecognizeRegionAsync_LoginSearchRegion_ShouldReturnAbsoluteCoordinates()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string targetText = "登入遊戲";

        var searchRegion = CoordinateUtilityServices.Rectangle.FromXYWH(
            600, 400, 1150, 550);

        byte[] screenshot = await ReadFixtureAsync(fixtureName);

        using var backend = CreateBackend();

        IOCRUtilityService service = new PaddleOcrUtilityService(backend);
        IOcrTextMatcher matcher = new OcrTextMatcher();

        OcrResult result = await service.RecognizeRegionAsync(
            screenshot,
            searchRegion,
            "zh-TW");

        OcrTextMatch match = matcher.FindUniqueMatch(
            result,
            targetText,
            OcrTextMatchMode.NormalizedContains);

        var bounds = match.Bounds;

        bounds.TopLeft.X.Should().BeGreaterThanOrEqualTo(
            searchRegion.TopLeft.X);

        bounds.TopLeft.Y.Should().BeGreaterThanOrEqualTo(
            searchRegion.TopLeft.Y);

        bounds.BottomRight.X.Should().BeLessThanOrEqualTo(
            searchRegion.BottomRight.X);

        bounds.BottomRight.Y.Should().BeLessThanOrEqualTo(
            searchRegion.BottomRight.Y);

        double centerX = bounds.TopLeft.X + bounds.Width / 2.0;
        double centerY = bounds.TopLeft.Y + bounds.Height / 2.0;

        centerX.Should().BeInRange(
            searchRegion.TopLeft.X,
            searchRegion.BottomRight.X);

        centerY.Should().BeInRange(
            searchRegion.TopLeft.Y,
            searchRegion.BottomRight.Y);

        _output.WriteLine(
            $"ABSOLUTE_COORDINATES: Fixture={fixtureName}, " +
            $"Bounds={bounds}, " +
            $"TextCenter=({centerX:G9},{centerY:G9})");
    }


    /// <summary>
    /// Creates a real ONNX inference backend from explicit test asset paths.
    /// </summary>
    private PaddleOcrOnnxBackend CreateBackend()
    {
        string detection = RequireFile("PADDLEOCR_DETECTION_MODEL");
        string recognition = RequireFile("PADDLEOCR_RECOGNITION_MODEL");
        string dictionary = RequireFile("PADDLEOCR_DICTIONARY");

        var options = new PaddleOcrOptions
        {
            DetectionModelPath = detection,
            RecognitionModelPath = recognition,
            DictionaryPath = dictionary,
            DetectionSize = ReadPositiveInt("PADDLEOCR_DETECTION_SIZE", 960),
            RecognitionWidth = ReadPositiveInt("PADDLEOCR_RECOGNITION_WIDTH", 320),
            RecognitionHeight = ReadPositiveInt("PADDLEOCR_RECOGNITION_HEIGHT", 48)
        };

        return new PaddleOcrOnnxBackend(options);
    }

    /// <summary>
    /// Reads a required asset path and fails with actionable configuration guidance.
    /// </summary>
    private string RequireFile(string environmentVariable)
    {
        string? path = Environment.GetEnvironmentVariable(environmentVariable);
        path.Should().NotBeNullOrWhiteSpace(
            $"set {environmentVariable} to the corresponding PaddleOCR test asset path");
        _fileSystem.File.Exists(path!).Should().BeTrue(
            $"{environmentVariable} must reference an existing file: '{path}'");
        return path!;
    }

    /// <summary>
    /// Reads a positive integer configuration value.
    /// </summary>
    private static int ReadPositiveInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        int.TryParse(value, out int result).Should().BeTrue(
            $"{name} must contain a valid integer");
        result.Should().BeGreaterThan(0);
        return result;
    }

    /// <summary>
    /// Reads a diagnostic screenshot through the file-system abstraction.
    /// </summary>
    private async Task<byte[]> ReadFixtureAsync(string fixtureName)
    {
        string? directory = Environment.GetEnvironmentVariable("PADDLEOCR_FIXTURE_DIR");
        directory.Should().NotBeNullOrWhiteSpace(
            "set PADDLEOCR_FIXTURE_DIR to the extracted OCR diagnostic screenshot folder");
        string path = _fileSystem.Path.Combine(directory!, fixtureName);
        _fileSystem.File.Exists(path).Should().BeTrue(
            $"the real screenshot fixture must exist at '{path}'");
        return await _fileSystem.File.ReadAllBytesAsync(path);
    }

    /// <summary>
    /// Normalizes OCR text while retaining character order.
    /// </summary>
    private static string Normalize(string text)
    {
        return string.Concat(text.AsSpan().ToString().Where(c => !char.IsWhiteSpace(c)));
    }

    /// <summary>
    /// Writes OCR output for model compatibility and geometry diagnosis.
    /// </summary>
    private void WriteDiagnostics(string name, OcrResult result, TimeSpan elapsed)
    {
        _output.WriteLine($"Fixture={name}, ElapsedMs={elapsed.TotalMilliseconds:F0}");
        _output.WriteLine($"Text=[{result.Text}], Lines={result.Lines.Count}");
        foreach (OcrTextLine line in result.Lines)
        {
            _output.WriteLine(
                $"Line=[{line.Text}], Bounds={line.Bounds}, Words={line.Words.Count}");
        }
    }
}
