using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;

namespace OCRUtilityServices.Tests.Models;

public sealed class OcrTextLineTests
{
    [Fact]
    public void Constructor_WithoutWords_ShouldCreateEmptyWordCollection()
    {
        // Arrange
        Rectangle bounds = Rectangle.FromXYWH(
            10,
            20,
            100,
            40);

        // Act
        OcrTextLine sut = new(
            "任務",
            bounds);

        // Assert
        sut.Text.Should().Be("任務");
        sut.Bounds.Should().Be(bounds);
        sut.Words.Should().NotBeNull();
        sut.Words.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithWords_ShouldPreserveWordInformation()
    {
        // Arrange
        Rectangle lineBounds = Rectangle.FromXYWH(
            10,
            20,
            200,
            40);

        OcrTextWord firstWord = new(
            "任",
            Rectangle.FromXYWH(
                150,
                20,
                20,
                40));

        OcrTextWord secondWord = new(
            "務",
            Rectangle.FromXYWH(
                175,
                20,
                20,
                40));

        IReadOnlyList<OcrTextWord> words =
        [
            firstWord,
            secondWord
        ];

        // Act
        OcrTextLine sut = new(
            "任 務",
            lineBounds,
            words);

        // Assert
        sut.Text.Should().Be("任 務");
        sut.Bounds.Should().Be(lineBounds);
        sut.Words.Should().Equal(
            firstWord,
            secondWord);
    }

    [Fact]
    public void Constructor_WhenTextIsNull_ShouldThrowExpectedException()
    {
        // Arrange
        Rectangle bounds = Rectangle.FromXYWH(
            10,
            20,
            100,
            40);

        // Act
        Action act = () => new OcrTextLine(
            null!,
            bounds);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*text*");
    }

    [Fact]
    public void Constructor_WhenWordsIsNull_ShouldThrowExpectedException()
    {
        // Arrange
        Rectangle bounds = Rectangle.FromXYWH(
            10,
            20,
            100,
            40);

        // Act
        Action act = () => new OcrTextLine(
            "任務",
            bounds,
            null!);

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*words*");
    }
}