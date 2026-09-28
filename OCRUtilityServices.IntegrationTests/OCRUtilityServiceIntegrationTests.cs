using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using System.IO.Abstractions;
using Xunit;
using Windows.Media.Ocr;
using OcrResult = OCRUtilityServices.Models.OcrResult;
using Xunit.Abstractions;

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
        OCRUtilityServices.Models.OcrResult result)
    {
        _output.WriteLine(
            $"OCR Text: [{result.Text}]");

        _output.WriteLine(
            $"OCR Line Count: {result.Lines.Count}");

        for (int index = 0;
             index < result.Lines.Count;
             index++)
        {
            OcrTextLine line = result.Lines[index];

            _output.WriteLine(
                $"Line[{index}]: " +
                $"Text=[{line.Text}], " +
                $"Bounds={line.Bounds}");
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

    private static string RemoveWhiteSpace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return string.Concat(
            value.Where(character =>
                !char.IsWhiteSpace(character)));
    }

}