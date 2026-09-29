
using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.Tests;

public sealed class OCRUtilityServiceTests
{
    private readonly IOCRUtilityService _service =
        new OCRUtilityService();

    [Fact]
    public async Task RecognizeAsync_NullImageBuffer_ShouldThrowArgumentNullException()
    {
        // Arrange
        byte[] imageBuffer = null!;

        Func<Task> act = () => _service.RecognizeAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<ArgumentNullException>()
            .WithMessage("*imageBuffer*");
    }

    [Fact]
    public async Task RecognizeAsync_EmptyImageBuffer_ShouldThrowArgumentException()
    {
        // Arrange
        byte[] imageBuffer = [];

        Func<Task> act = () => _service.RecognizeAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*The image buffer must not be empty.*");
    }

    [Fact]
    public async Task RecognizeAsync_CanceledToken_ShouldThrowOperationCanceledException()
    {
        // Arrange
        byte[] imageBuffer = [0x01];

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        Func<Task> act = () => _service.RecognizeAsync(
            imageBuffer,
            cancellationTokenSource.Token);

        // Act & Assert
        await act.Should()
            .ThrowAsync<OperationCanceledException>()
            .WithMessage("*canceled*");
    }

    [Fact]
    public async Task QuickOcrAsync_NullImageBuffer_ShouldThrowArgumentNullException()
    {
        // Arrange
        byte[] imageBuffer = null!;

        Func<Task> act = () => _service.QuickOcrAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<ArgumentNullException>()
            .WithMessage("*imageBuffer*");
    }

    [Fact]
    public async Task QuickOcrAsync_EmptyImageBuffer_ShouldThrowArgumentException()
    {
        // Arrange
        byte[] imageBuffer = [];

        Func<Task> act = () => _service.QuickOcrAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*The image buffer must not be empty.*");
    }

    [Fact]
    public async Task RecognizeAsync_NonWindowsTarget_ShouldThrowPlatformNotSupportedException()
    {
        // Arrange
        byte[] imageBuffer = [0x01];

        Func<Task> act = () => _service.RecognizeAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<PlatformNotSupportedException>()
            .WithMessage(
                "*This OCR implementation requires the Windows target framework.*");
    }

    [Fact]
    public async Task QuickOcrAsync_NonWindowsTarget_ShouldThrowPlatformNotSupportedException()
    {
        // Arrange
        byte[] imageBuffer = [0x01];

        Func<Task> act = () => _service.QuickOcrAsync(imageBuffer);

        // Act & Assert
        await act.Should()
            .ThrowAsync<PlatformNotSupportedException>()
            .WithMessage(
                "*This OCR implementation requires the Windows target framework.*");
    }

    [Fact]
    public async Task RecognizeAsync_RegionWithoutLanguageTag_ShouldThrowArgumentException()
    {
        // Arrange
        var sut = new OCRUtilityService();

        byte[] imageBuffer = [0x01];

        var options = new OcrRecognitionOptions
        {
            Region = Rectangle.FromXYWH(
                500,
                840,
                130,
                120)
        };

        // Act
        Func<Task> act = () =>
            sut.RecognizeAsync(
                imageBuffer,
                options);

        // Assert
        await act.Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage(
                "*language tag is required*");
    }
}