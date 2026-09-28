
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using System.IO.Abstractions;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class WindowsOcrIntegrationTests
{
    private const string FixtureFileName = "ocr-hello.png";
    private const int FixtureWidth = 800;
    private const int FixtureHeight = 200;

    private readonly IFileSystem _fileSystem;
    private readonly IOCRUtilityService _service;

    public WindowsOcrIntegrationTests()
    {
        _fileSystem = new FileSystem();
        _service = new OCRUtilityService();
    }

    [Fact]
    [Trait("Category", "WindowsOcrIntegration")]
    public async Task RecognizeAsync_ValidPng_ShouldRecognizeExpectedText()
    {
        // Arrange
        byte[] imageBuffer = ReadFixture();

        // Act
        OcrResult result = await _service.RecognizeAsync(
            imageBuffer
        );

        // Assert
        result.Should().NotBeNull();

        string normalizedText = NormalizeWhitespace(result.Text);

        normalizedText.Should().ContainEquivalentOf("HELLO");
        normalizedText.Should().ContainEquivalentOf("OCR");
    }

    [Fact]
    [Trait("Category", "WindowsOcrIntegration")]
    public async Task RecognizeAsync_ValidPng_ShouldReturnTextLineBounds()
    {
        // Arrange
        byte[] imageBuffer = ReadFixture();

        // Act
        OcrResult result = await _service.RecognizeAsync(
            imageBuffer
        );

        // Assert
        result.Lines.Should().NotBeEmpty();

        OcrTextLine textLine = result.Lines
            .First(line =>
                NormalizeWhitespace(line.Text)
                    .Contains(
                        "HELLO",
                        StringComparison.OrdinalIgnoreCase));

        textLine.Bounds.Width.Should().BeGreaterThan(0);
        textLine.Bounds.Height.Should().BeGreaterThan(0);

        textLine.Bounds.TopLeft.X.Should().BeGreaterThanOrEqualTo(0);
        textLine.Bounds.TopLeft.Y.Should().BeGreaterThanOrEqualTo(0);

        textLine.Bounds.BottomRight.X.Should()
            .BeLessThanOrEqualTo(FixtureWidth);

        textLine.Bounds.BottomRight.Y.Should()
            .BeLessThanOrEqualTo(FixtureHeight);

        textLine.Bounds.Center.X.Should()
            .BeInRange(0, FixtureWidth);

        textLine.Bounds.Center.Y.Should()
            .BeInRange(0, FixtureHeight);
    }

    [Fact]
    [Trait("Category", "WindowsOcrIntegration")]
    public async Task QuickOcrAsync_ValidPng_ShouldReturnRecognizedText()
    {
        // Arrange
        byte[] imageBuffer = ReadFixture();

        // Act
        string text = await _service.QuickOcrAsync(imageBuffer);

        // Assert
        string normalizedText = NormalizeWhitespace(text);

        normalizedText.Should().ContainEquivalentOf("HELLO");
        normalizedText.Should().ContainEquivalentOf("OCR");
    }

    [Fact]
    [Trait("Category", "WindowsOcrIntegration")]
    public async Task RecognizeAsync_InvalidImage_ShouldThrowDecodeException()
    {
        // Arrange
        byte[] imageBuffer = [0x01, 0x02, 0x03, 0x04];

        Func<Task> act = async () =>
            await _service.RecognizeAsync(imageBuffer);

        // Act & Assert
        await act.Should().ThrowAsync<Exception>();
    }

    private byte[] ReadFixture()
    {
        string path = _fileSystem.Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            FixtureFileName);

        _fileSystem.File.Exists(path).Should().BeTrue(
            $"the OCR integration test fixture must exist at '{path}'");

        return _fileSystem.File.ReadAllBytes(path);
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }
}