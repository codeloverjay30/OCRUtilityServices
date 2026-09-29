using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using System.IO.Abstractions;
using Xunit;
using Windows.Media.Ocr;
using OcrResult = OCRUtilityServices.Models.OcrResult;
using Xunit.Abstractions;
using CoordinateUtilityServices;

namespace OCRUtilityServices.IntegrationTests;

public sealed class OCRUtilityServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IFileSystem _fileSystem;

    public OCRUtilityServiceIntegrationTests(
        ITestOutputHelper output)
    {
        _output = output;
        _fileSystem = new FileSystem();
    }

    [Fact]
    public async Task RecognizeAsync_GameScreenshot_ShouldRecognizeVisibleText()
    {
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the OCR integration test image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(
                imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the OCR integration test image must contain image data");

        var sut = new OCRUtilityService();

        OcrResult result =
            await sut.RecognizeAsync(
                imageBuffer);

        WriteDiagnosticOutput(result);

        result
            .Should()
            .NotBeNull();

        result.Text
            .Should()
            .NotBeNullOrWhiteSpace(
                "the game screenshot contains multiple clearly visible text labels");

        result.Lines
            .Should()
            .NotBeEmpty(
                "visible text in the game screenshot should produce OCR text lines");
    }

    private void WriteDiagnosticOutput(
        OcrResult result)
    {
        _output.WriteLine(
            $"OCR Text: [{result.Text}]");

        _output.WriteLine(
            $"OCR Line Count: {result.Lines.Count}");

        for (int lineIndex = 0;
             lineIndex < result.Lines.Count;
             lineIndex++)
        {
            OcrTextLine line = result.Lines[lineIndex];

            _output.WriteLine(
                $"Line[{lineIndex}]: " +
                $"Text=[{line.Text}], " +
                $"Bounds={line.Bounds}, " +
                $"WordCount={line.Words.Count}");

            for (int wordIndex = 0;
                 wordIndex < line.Words.Count;
                 wordIndex++)
            {
                OcrTextWord word =
                    line.Words[wordIndex];

                _output.WriteLine(
                    $"  Word[{wordIndex}]: " +
                    $"Text=[{word.Text}], " +
                    $"Bounds={word.Bounds}");
            }
        }
    }


    [Fact]
    public async Task RecognizeAsync_GameScreenshot_ShouldRecognizeTaskText()
    {
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the OCR integration test image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the OCR integration test image must contain image data");

        var sut = new OCRUtilityService();

        OcrResult result =
            await sut.RecognizeAsync(imageBuffer);

        WriteDiagnosticOutput(result);

        result.Lines
            .Should()
            .Contain(
                line => RemoveWhiteSpace(line.Text).Contains(
                    "任務",
                    StringComparison.Ordinal),
                "the game screenshot visibly contains the task menu text '任務'");
    }

    [Fact]
    public void AvailableRecognizerLanguages_ShouldIncludeTraditionalChinese()
    {
        string[] languages =
            OcrEngine.AvailableRecognizerLanguages
                .Select(language => language.LanguageTag)
                .ToArray();

        _output.WriteLine(
            $"Available OCR languages: [{string.Join(", ", languages)}]");

        languages
            .Should()
            .Contain(
                languageTag =>
                    languageTag.StartsWith(
                        "zh-Hant",
                        StringComparison.OrdinalIgnoreCase) ||
                    languageTag.Equals(
                        "zh-TW",
                        StringComparison.OrdinalIgnoreCase),
                "Traditional Chinese OCR recognition is required for the target game");
    }

    [Fact]
    public async Task RecognizeAsync_TaskRegion_ShouldRecognizeTaskText()
    {
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen-task-roi.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the OCR task-region test image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the OCR task-region test image must contain image data");

        var sut = new OCRUtilityService();

        OcrResult result =
            await sut.RecognizeAsync(imageBuffer);

        WriteDiagnosticOutput(result);

        result.Lines
            .Should()
            .Contain(
                line => RemoveWhiteSpace(line.Text).Contains(
                    "任務",
                    StringComparison.Ordinal),
                "the cropped task region visibly contains the text '任務'");
    }

    [Fact]
    public async Task RecognizeAsync_TaskRegionWith3xScale_ShouldRecognizeTaskText()
    {
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen-task-roi-3x.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the OCR task-region test image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the OCR task-region test image must contain image data");

        var sut = new OCRUtilityService();

        OcrResult result =
            await sut.RecognizeAsync(imageBuffer);

        WriteDiagnosticOutput(result);

        result.Lines
            .Should()
            .Contain(
                line => RemoveWhiteSpace(line.Text).Contains(
                    "任務",
                    StringComparison.Ordinal),
                "the cropped task region visibly contains the text '任務'");
    }

    /// <summary>
    /// 這個Integration test的目的是要驗證OCRUtilityService在辨識任務文字時，
    /// 是否能夠正確辨識出任務文字，並且保留每個文字的幾何資訊。這個測試使用了
    /// OmniAppium runtime的截圖作為測試資料，並且使用繁體中文作為辨識語言。
    /// 測試中會檢查OCR結果中是否包含`任務`文字，並且檢查每個文字的幾何資訊是否正確。
    /// </summary>
    /// <returns></returns>
    /// <remarks>
    /// 這個Integration test說明`full screenshot 一定能辨識任務這兩個字`這個假設未必成立。
    /// </remarks>
    [Fact]
    public async Task RecognizeAsync_OmniAppiumRuntimeScreenshot_WithTraditionalChinese_ShouldReturnRecognizedLines()
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(
                imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeAsync(
                imageBuffer,
                "zh-TW");

        WriteDiagnosticOutput(result);

        // Assert
        result.Text
            .Should()
            .NotBeNullOrWhiteSpace(
                "the production screenshot should produce OCR text");

        result.Lines
            .Should()
            .NotBeEmpty(
                "the production screenshot should produce OCR lines");

        result.Lines
            .Should()
            .OnlyContain(
                line =>
                    !string.IsNullOrWhiteSpace(line.Text)
                    && line.Bounds.Width > 0
                    && line.Bounds.Height > 0,
                "every recognized line must contain usable text and geometry");

        result.Lines
            .SelectMany(line => line.Words)
            .Should()
            .NotBeEmpty(
                "the production screenshot should preserve word-level OCR geometry");

        result.Lines
            .SelectMany(line => line.Words)
            .Should()
            .OnlyContain(
                word =>
                    !string.IsNullOrWhiteSpace(word.Text)
                    && word.Bounds.Width > 0
                    && word.Bounds.Height > 0,
                "every recognized word must contain usable text and geometry");

    }

    [Fact]
    public async Task RecognizeAsync_TaskRegion_WithTraditionalChinese_ShouldRecognizeTaskText()
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen-task-roi.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the task ROI fixture must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(
                imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the task ROI fixture must contain image data");

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeAsync(
                imageBuffer,
                "zh-TW");

        WriteDiagnosticOutput(result);

        // Assert
        OcrTextLine[] taskLines = result.Lines
            .Where(
                line =>
                    RemoveWhiteSpace(line.Text)
                        .Contains(
                            "任務",
                            StringComparison.Ordinal))
            .ToArray();

        taskLines
            .Should()
            .ContainSingle(
                "the task ROI visibly contains exactly one '任務' target");

        OcrTextLine taskLine =
            taskLines.Single();

        taskLine.Words
            .Should()
            .NotBeEmpty(
                "the recognized task target must preserve word-level geometry");

        taskLine.Words
            .Should()
            .OnlyContain(
                word =>
                    !string.IsNullOrWhiteSpace(word.Text)
                    && word.Bounds.Width > 0
                    && word.Bounds.Height > 0,
                "recognized task words must contain usable text and geometry");
    }

    [Fact]
    public async Task DecodeRegion_OmniAppiumRuntimeScreenshot_ShouldProduceExpectedTaskRegion()
    {
        // Arrange
        string sourceImagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        string diagnosticImagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "actual-task-region.png");

        _fileSystem.File
            .Exists(sourceImagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{sourceImagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(
                sourceImagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        const int regionX = 500;
        const int regionY = 840;
        const int regionWidth = 130;
        const int regionHeight = 100;


        // Act
        using var inputStream =
            new InMemoryRandomAccessStream();

        await inputStream.WriteAsync(
            imageBuffer.AsBuffer());

        inputStream.Seek(0);

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(
                inputStream);

        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = regionX,
                Y = regionY,
                Width = regionWidth,
                Height = regionHeight
            }
        };

        using SoftwareBitmap regionBitmap =
            await decoder.GetSoftwareBitmapAsync(
                decoder.BitmapPixelFormat,
                decoder.BitmapAlphaMode,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

        using var outputStream =
            new InMemoryRandomAccessStream();

        BitmapEncoder encoder =
            await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                outputStream);

        encoder.SetSoftwareBitmap(
            regionBitmap);

        await encoder.FlushAsync();

        outputStream.Seek(0);

        byte[] diagnosticImageBuffer =
            new byte[checked((int)outputStream.Size)];

        using (Stream readStream = outputStream.AsStreamForRead())
        {
            int totalBytesRead = 0;

            while (totalBytesRead < diagnosticImageBuffer.Length)
            {
                int bytesRead =
                    await readStream.ReadAsync(
                        diagnosticImageBuffer.AsMemory(
                            totalBytesRead,
                            diagnosticImageBuffer.Length - totalBytesRead));

                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
            }

            totalBytesRead
                .Should()
                .Be(
                    diagnosticImageBuffer.Length,
                    "the complete encoded diagnostic ROI must be read");
        }

        await _fileSystem.File.WriteAllBytesAsync(
            diagnosticImagePath,
            diagnosticImageBuffer);

        // Assert
        regionBitmap.PixelWidth
            .Should()
            .Be(
                regionWidth,
                "the decoded ROI width must match the requested recognition region");

        regionBitmap.PixelHeight
            .Should()
            .Be(
                regionHeight,
                "the decoded ROI height must match the requested recognition region");

        diagnosticImageBuffer
            .Should()
            .NotBeEmpty(
                "the decoded ROI must be encoded into a diagnostic PNG");

        _fileSystem.File
            .Exists(diagnosticImagePath)
            .Should()
            .BeTrue(
                $"the diagnostic ROI image must be written to '{diagnosticImagePath}'");

        Console.WriteLine(
            $"Source image: {decoder.PixelWidth}x{decoder.PixelHeight}");

        Console.WriteLine(
            $"Requested ROI: X={regionX}, Y={regionY}, " +
            $"Width={regionWidth}, Height={regionHeight}");

        Console.WriteLine(
            $"Decoded ROI: {regionBitmap.PixelWidth}x{regionBitmap.PixelHeight}");

        Console.WriteLine(
            $"Diagnostic ROI: {diagnosticImagePath}");

            var sut = new OCRUtilityService();

        OcrResult reDecodedResult =
            await sut.RecognizeAsync(
                diagnosticImageBuffer,
                "zh-TW");

        _output.WriteLine(
            $"Re-decoded OCR Text: [{reDecodedResult.Text}]");

        _output.WriteLine(
            $"Re-decoded OCR Line Count: {reDecodedResult.Lines.Count}");

        foreach (OcrTextLine line in reDecodedResult.Lines)
        {
            _output.WriteLine(
                $"Re-decoded OCR Line: [{line.Text}] " +
                $"Bounds=({line.Bounds.TopLeft.X}, {line.Bounds.TopLeft.Y})-" +
                $"({line.Bounds.BottomRight.X}, {line.Bounds.BottomRight.Y})");
        }
    }

    /// <summary>
    /// Determines whether scaling the OmniAppium runtime task region improves
    /// Traditional Chinese OCR recognition.
    /// </summary>
    /// <param name="scaleFactor">
    /// The scale factor applied to the decoded task region before recognition.
    /// </param>
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(4.0)]
    public async Task RecognizeAsync_OmniAppiumRuntimeTaskRegion_WithScale_ShouldReportRecognitionResult(
        double scaleFactor)
    {
        // Arrange
        string sourceImagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(sourceImagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{sourceImagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(
                sourceImagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        const int regionX = 500;
        const int regionY = 840;
        const int regionWidth = 130;
        const int regionHeight = 100;

        byte[] scaledRegionBuffer =
            await DecodeAndScaleRegionAsync(
                imageBuffer,
                regionX,
                regionY,
                regionWidth,
                regionHeight,
                scaleFactor);

        string diagnosticImagePath =
            _fileSystem.Path.Combine(
                AppContext.BaseDirectory,
                $"actual-task-region-{scaleFactor:0}x.png");

        await _fileSystem.File.WriteAllBytesAsync(
            diagnosticImagePath,
            scaledRegionBuffer);

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeAsync(
                scaledRegionBuffer,
                "zh-TW");

        // Assert
        scaledRegionBuffer
            .Should()
            .NotBeEmpty(
                "the scaled task region must contain encoded image data");

        result
            .Should()
            .NotBeNull();

        bool recognizedTask =
            result.Lines.Any(
                line =>
                    RemoveWhiteSpace(line.Text)
                        .Contains(
                            "任務",
                            StringComparison.Ordinal));

        _output.WriteLine(
            $"Scale: {scaleFactor:0.##}x");

        _output.WriteLine(
            $"OCR Text: [{result.Text}]");

        _output.WriteLine(
            $"OCR Line Count: {result.Lines.Count}");

        _output.WriteLine(
            $"Recognized 任務: {recognizedTask}");

        _output.WriteLine(
            $"Diagnostic image: {diagnosticImagePath}");

        foreach (OcrTextLine line in result.Lines)
        {
            _output.WriteLine(
                $"Line: [{line.Text}], " +
                $"Bounds=({line.Bounds.TopLeft.X}, {line.Bounds.TopLeft.Y})-" +
                $"({line.Bounds.BottomRight.X}, {line.Bounds.BottomRight.Y})");
        }
    }

    /// <summary>
    /// Compares the known OCR-readable task fixture with the task region decoded
    /// from the OmniAppium runtime screenshot.
    /// </summary>
    [Fact]
    public async Task CompareTaskFixtureAndRuntimeRegion_ShouldReportImageCharacteristics()
    {
        // Arrange
        string fixturePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "game-main-screen-task-roi.png");

        string runtimePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File.Exists(fixturePath)
            .Should()
            .BeTrue(
                $"the known OCR-readable task fixture must exist at '{fixturePath}'");

        _fileSystem.File.Exists(runtimePath)
            .Should()
            .BeTrue(
                $"the runtime screenshot must exist at '{runtimePath}'");

        byte[] fixtureBuffer =
            await _fileSystem.File.ReadAllBytesAsync(fixturePath);

        byte[] runtimeBuffer =
            await _fileSystem.File.ReadAllBytesAsync(runtimePath);

        fixtureBuffer.Should().NotBeEmpty();
        runtimeBuffer.Should().NotBeEmpty();

        // Act
        ImageDiagnostic fixture =
            await DecodeImageDiagnosticAsync(fixtureBuffer);

        byte[] runtimeRegionBuffer =
            await DecodeAndScaleRegionAsync(
                runtimeBuffer,
                regionX: 500,
                regionY: 840,
                regionWidth: 130,
                regionHeight: 100,
                scaleFactor: 1.0);

        ImageDiagnostic runtimeRegion =
            await DecodeImageDiagnosticAsync(runtimeRegionBuffer);

        var sut = new OCRUtilityService();

        OcrResult fixtureOcr =
            await sut.RecognizeAsync(
                fixtureBuffer,
                "zh-TW");

        OcrResult runtimeOcr =
            await sut.RecognizeAsync(
                runtimeRegionBuffer,
                "zh-TW");

        // Assert
        fixture.Width.Should().BeGreaterThan(0);
        fixture.Height.Should().BeGreaterThan(0);

        runtimeRegion.Width.Should().Be(130);
        runtimeRegion.Height.Should().Be(100);

        _output.WriteLine(
            $"Fixture: {fixture.Width}x{fixture.Height}, " +
            $"PixelFormat={fixture.PixelFormat}, " +
            $"AlphaMode={fixture.AlphaMode}, " +
            $"Bytes={fixture.EncodedLength}");

        _output.WriteLine(
            $"Runtime ROI: {runtimeRegion.Width}x{runtimeRegion.Height}, " +
            $"PixelFormat={runtimeRegion.PixelFormat}, " +
            $"AlphaMode={runtimeRegion.AlphaMode}, " +
            $"Bytes={runtimeRegion.EncodedLength}");

        _output.WriteLine(
            $"Fixture OCR: [{fixtureOcr.Text}], Lines={fixtureOcr.Lines.Count}");

        _output.WriteLine(
            $"Runtime ROI OCR: [{runtimeOcr.Text}], Lines={runtimeOcr.Lines.Count}");
    }

    /// <summary>
    /// Verifies that recognition options route runtime screenshot recognition
    /// through the configured region while preserving source-image coordinates.
    /// </summary>
    [Fact]
    public async Task RecognizeAsync_OmniAppiumRuntimeScreenshot_WithRecognitionOptions_ShouldRecognizeTaskWithSourceCoordinates()
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        Rectangle searchRegion =
            Rectangle.FromXYWH(
                x: 500,
                y: 840,
                width: 130,
                height: 120);

        var options = new OcrRecognitionOptions
        {
            LanguageTag = "zh-TW",
            Region = searchRegion
        };

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeAsync(
                imageBuffer,
                options);

        // Assert
        OcrTextLine[] taskLines =
            result.Lines
                .Where(
                    line =>
                        RemoveWhiteSpace(line.Text)
                            .Contains(
                                "任務",
                                StringComparison.Ordinal))
                .ToArray();

        taskLines
            .Should()
            .ContainSingle(
                "the configured recognition region must contain exactly one task target");

        OcrTextLine taskLine = taskLines.Single();

        taskLine.Bounds.TopLeft.X
            .Should()
            .BeGreaterThanOrEqualTo(
                searchRegion.TopLeft.X);

        taskLine.Bounds.TopLeft.Y
            .Should()
            .BeGreaterThanOrEqualTo(
                searchRegion.TopLeft.Y);

        taskLine.Bounds.BottomRight.X
            .Should()
            .BeLessThanOrEqualTo(
                searchRegion.BottomRight.X);

        taskLine.Bounds.BottomRight.Y
            .Should()
            .BeLessThanOrEqualTo(
                searchRegion.BottomRight.Y);

        taskLine.Words
            .Should()
            .NotBeEmpty(
                "the recognized task must preserve word-level source geometry");

        taskLine.Words
            .Should()
            .OnlyContain(
                word =>
                    word.Bounds.TopLeft.X >= searchRegion.TopLeft.X
                    && word.Bounds.TopLeft.Y >= searchRegion.TopLeft.Y
                    && word.Bounds.BottomRight.X <= searchRegion.BottomRight.X
                    && word.Bounds.BottomRight.Y <= searchRegion.BottomRight.Y,
                "word bounds returned through the options overload must remain in source-image coordinates");
    }


    /// <summary>
    /// Describes image characteristics relevant to OCR diagnostics.
    /// </summary>
    private sealed record ImageDiagnostic(
        uint Width,
        uint Height,
        BitmapPixelFormat PixelFormat,
        BitmapAlphaMode AlphaMode,
        int EncodedLength);

    /// <summary>
    /// Decodes image metadata required for OCR fixture diagnostics.
    /// </summary>
    /// <param name="imageBuffer">
    /// The encoded image data to inspect.
    /// </param>
    /// <returns>
    /// The decoded image characteristics.
    /// </returns>
    private static async Task<ImageDiagnostic> DecodeImageDiagnosticAsync(
        byte[] imageBuffer)
    {
        ArgumentNullException.ThrowIfNull(imageBuffer);

        if (imageBuffer.Length == 0)
        {
            throw new ArgumentException(
                "The image buffer must not be empty.",
                nameof(imageBuffer));
        }

        using var stream =
            new InMemoryRandomAccessStream();

        await stream.WriteAsync(
            imageBuffer.AsBuffer());

        stream.Seek(0);

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(stream);

        using SoftwareBitmap bitmap =
            await decoder.GetSoftwareBitmapAsync();

        return new ImageDiagnostic(
            decoder.PixelWidth,
            decoder.PixelHeight,
            bitmap.BitmapPixelFormat,
            bitmap.BitmapAlphaMode,
            imageBuffer.Length);
    }

    /// <summary>
    /// Searches a larger runtime screenshot region to determine whether the task
    /// label is located outside the currently assumed recognition region.
    /// </summary>
    [Fact]
    public async Task RecognizeRegionAsync_OmniAppiumRuntimeScreenshot_WithExpandedTaskSearchRegion_ShouldReportRecognizedText()
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        Rectangle searchRegion = Rectangle.FromXYWH(
            x: 400,
            y: 730,
            width: 350,
            height: 350);

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeRegionAsync(
                imageBuffer,
                searchRegion,
                "zh-TW");

        // Assert
        result.Should().NotBeNull();

        _output.WriteLine(
            $"Search region: {searchRegion}");

        WriteDiagnosticOutput(result);

        bool recognizedTask =
            result.Lines.Any(
                line =>
                    RemoveWhiteSpace(line.Text)
                        .Contains(
                            "任務",
                            StringComparison.Ordinal));

        _output.WriteLine(
            $"Recognized 任務: {recognizedTask}");
    }

    /// <summary>
    /// Determines how much surrounding source-image context is required for
    /// reliable recognition of the runtime task label.
    /// </summary>
    /// <param name="x">The source-image X coordinate of the recognition region.</param>
    /// <param name="y">The source-image Y coordinate of the recognition region.</param>
    /// <param name="width">The recognition-region width.</param>
    /// <param name="height">The recognition-region height.</param>
    /// <param name="scenario">The diagnostic scenario name.</param>
    [Theory]
    [InlineData(500, 840, 130, 100, "Original")]
    [InlineData(500, 840, 130, 120, "Bottom+20")]
    [InlineData(500, 820, 130, 140, "Vertical+20")]
    [InlineData(480, 820, 170, 140, "Margin20")]
    [InlineData(460, 800, 210, 180, "Margin40")]
    [InlineData(400, 730, 350, 350, "ExpandedKnownGood")]
    public async Task RecognizeRegionAsync_OmniAppiumRuntimeScreenshot_WithDifferentMargins_ShouldReportTaskRecognition(
        int x,
        int y,
        int width,
        int height,
        string scenario)
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        Rectangle region =
            Rectangle.FromXYWH(
                x,
                y,
                width,
                height);

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeRegionAsync(
                imageBuffer,
                region,
                "zh-TW");

        // Assert
        result.Should().NotBeNull();

        bool recognizedTask =
            result.Lines.Any(
                line =>
                    RemoveWhiteSpace(line.Text)
                        .Contains(
                            "任務",
                            StringComparison.Ordinal));

        _output.WriteLine(
            $"Scenario: {scenario}");

        _output.WriteLine(
            $"Region: {region}");

        _output.WriteLine(
            $"Recognized 任務: {recognizedTask}");

        WriteDiagnosticOutput(result);
    }


    /// <summary>
    /// Verifies that the runtime task search region recognizes the task label
    /// and returns usable source-image geometry for the target words.
    /// </summary>
    [Fact]
    public async Task RecognizeRegionAsync_OmniAppiumRuntimeScreenshot_WithTaskSearchRegion_ShouldRecognizeTaskWithSourceCoordinates()
    {
        // Arrange
        string imagePath = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "omniappium-runtime-ocr-diagnostic.png");

        _fileSystem.File
            .Exists(imagePath)
            .Should()
            .BeTrue(
                $"the runtime OCR diagnostic image must exist at '{imagePath}'");

        byte[] imageBuffer =
            await _fileSystem.File.ReadAllBytesAsync(imagePath);

        imageBuffer
            .Should()
            .NotBeEmpty(
                "the runtime OCR diagnostic image must contain image data");

        Rectangle searchRegion =
            Rectangle.FromXYWH(
                x: 500,
                y: 840,
                width: 130,
                height: 120);

        var sut = new OCRUtilityService();

        // Act
        OcrResult result =
            await sut.RecognizeRegionAsync(
                imageBuffer,
                searchRegion,
                "zh-TW");

        // Assert
        OcrTextLine[] taskLines =
            result.Lines
                .Where(
                    line =>
                        RemoveWhiteSpace(line.Text)
                            .Contains(
                                "任務",
                                StringComparison.Ordinal))
                .ToArray();

        taskLines
            .Should()
            .ContainSingle(
                "the verified runtime search region must contain exactly one task target");

        OcrTextLine taskLine = taskLines.Single();

        taskLine.Bounds.TopLeft.X
            .Should()
            .BeGreaterThanOrEqualTo(searchRegion.TopLeft.X);

        taskLine.Bounds.TopLeft.Y
            .Should()
            .BeGreaterThanOrEqualTo(searchRegion.TopLeft.Y);

        taskLine.Bounds.BottomRight.X
            .Should()
            .BeLessThanOrEqualTo(searchRegion.BottomRight.X);

        taskLine.Bounds.BottomRight.Y
            .Should()
            .BeLessThanOrEqualTo(searchRegion.BottomRight.Y);

        taskLine.Words
            .Should()
            .NotBeEmpty(
                "the target must expose word-level geometry for absolute-coordinate interaction");

        taskLine.Words
            .Should()
            .OnlyContain(
                word =>
                    word.Bounds.Width > 0
                    && word.Bounds.Height > 0,
                "every target word must expose usable geometry");
    }

    /// <summary>
    /// Decodes a source-image region, then scales the decoded region in a
    /// separate imaging operation and encodes the result as PNG data.
    /// </summary>
    /// <param name="imageBuffer">
    /// The encoded source image.
    /// </param>
    /// <param name="regionX">
    /// The horizontal source-image coordinate of the region.
    /// </param>
    /// <param name="regionY">
    /// The vertical source-image coordinate of the region.
    /// </param>
    /// <param name="regionWidth">
    /// The source-image width of the region.
    /// </param>
    /// <param name="regionHeight">
    /// The source-image height of the region.
    /// </param>
    /// <param name="scaleFactor">
    /// The scale factor applied after decoding the source-image region.
    /// </param>
    /// <returns>
    /// PNG-encoded image data containing the scaled recognition region.
    /// </returns>
    private static async Task<byte[]> DecodeAndScaleRegionAsync(
        byte[] imageBuffer,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        double scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(imageBuffer);

        if (imageBuffer.Length == 0)
        {
            throw new ArgumentException(
                "The image buffer must not be empty.",
                nameof(imageBuffer));
        }

        if (regionX < 0
            || regionY < 0
            || regionWidth <= 0
            || regionHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionWidth),
                "The recognition region must contain valid non-negative coordinates and positive dimensions.");
        }

        if (!double.IsFinite(scaleFactor)
            || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleFactor),
                scaleFactor,
                "The scale factor must be a finite value greater than zero.");
        }

        uint scaledWidth =
            checked((uint)Math.Round(
                regionWidth * scaleFactor,
                MidpointRounding.AwayFromZero));

        uint scaledHeight =
            checked((uint)Math.Round(
                regionHeight * scaleFactor,
                MidpointRounding.AwayFromZero));

        using var sourceStream =
            new InMemoryRandomAccessStream();

        await sourceStream.WriteAsync(
            imageBuffer.AsBuffer());

        sourceStream.Seek(0);

        BitmapDecoder sourceDecoder =
            await BitmapDecoder.CreateAsync(
                sourceStream);

        var cropTransform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = checked((uint)regionX),
                Y = checked((uint)regionY),
                Width = checked((uint)regionWidth),
                Height = checked((uint)regionHeight)
            }
        };

        using SoftwareBitmap croppedBitmap =
            await sourceDecoder.GetSoftwareBitmapAsync(
                sourceDecoder.BitmapPixelFormat,
                sourceDecoder.BitmapAlphaMode,
                cropTransform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

        croppedBitmap.PixelWidth
            .Should()
            .Be(
                regionWidth,
                "the first decoding stage must preserve the requested ROI width");

        croppedBitmap.PixelHeight
            .Should()
            .Be(
                regionHeight,
                "the first decoding stage must preserve the requested ROI height");

        using var croppedStream =
            new InMemoryRandomAccessStream();

        BitmapEncoder croppedEncoder =
            await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                croppedStream);

        croppedEncoder.SetSoftwareBitmap(
            croppedBitmap);

        await croppedEncoder.FlushAsync();

        croppedStream.Seek(0);

        BitmapDecoder croppedDecoder =
            await BitmapDecoder.CreateAsync(
                croppedStream);

        var scaleTransform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode =
                BitmapInterpolationMode.Cubic
        };

        using SoftwareBitmap scaledBitmap =
            await croppedDecoder.GetSoftwareBitmapAsync(
                croppedDecoder.BitmapPixelFormat,
                croppedDecoder.BitmapAlphaMode,
                scaleTransform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

        scaledBitmap.PixelWidth
            .Should()
            .Be(
                checked((int)scaledWidth),
                "the scaled OCR bitmap must have the requested width");

        scaledBitmap.PixelHeight
            .Should()
            .Be(
                checked((int)scaledHeight),
                "the scaled OCR bitmap must have the requested height");

        using var outputStream =
            new InMemoryRandomAccessStream();

        BitmapEncoder outputEncoder =
            await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                outputStream);

        outputEncoder.SetSoftwareBitmap(
            scaledBitmap);

        await outputEncoder.FlushAsync();

        outputStream.Seek(0);

        byte[] outputBuffer =
            new byte[checked((int)outputStream.Size)];

        using Stream readStream =
            outputStream.AsStreamForRead();

        int totalBytesRead = 0;

        while (totalBytesRead < outputBuffer.Length)
        {
            int bytesRead =
                await readStream.ReadAsync(
                    outputBuffer.AsMemory(
                        totalBytesRead,
                        outputBuffer.Length - totalBytesRead));

            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        totalBytesRead
            .Should()
            .Be(
                outputBuffer.Length,
                "the complete scaled OCR diagnostic image must be read");

        return outputBuffer;
    }


    private static string RemoveWhiteSpace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return string.Concat(
            value.Where(character =>
                !char.IsWhiteSpace(character)));
    }

}