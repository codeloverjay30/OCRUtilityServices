using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Internal;

namespace OCRUtilityServices.Tests;

public sealed class OcrRegionGeometryTests
{
    [Fact]
    public void TranslateToSourceCoordinates_WithLocalBounds_ReturnsOffsetBounds()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            400,
            630,
            200,
            180);

        Rectangle localBounds = Rectangle.FromXYWH(
            60,
            70,
            40,
            20);

        // Act
        Rectangle actual =
            OcrRegionGeometry.TranslateToSourceCoordinates(
                localBounds,
                region);

        // Assert
        actual.TopLeft.X.Should().Be(460);
        actual.TopLeft.Y.Should().Be(700);
        actual.Width.Should().Be(40);
        actual.Height.Should().Be(20);
    }

    [Fact]
    public void ValidateWithinImage_WhenRegionFitsImage_DoesNotThrow()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            400,
            630,
            200,
            180);

        // Act
        Action act = () =>
            OcrRegionGeometry.ValidateWithinImage(
                region,
                imageWidth: 2048,
                imageHeight: 945);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateWithinImage_WhenRegionExceedsRightBoundary_ThrowsExpectedException()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            2000,
            100,
            100,
            100);

        // Act
        Action act = () =>
            OcrRegionGeometry.ValidateWithinImage(
                region,
                imageWidth: 2048,
                imageHeight: 945);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*outside the source image bounds*");
    }

    [Fact]
    public void ValidateWithinImage_WhenRegionExceedsBottomBoundary_ThrowsExpectedException()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            100,
            900,
            100,
            100);

        // Act
        Action act = () =>
            OcrRegionGeometry.ValidateWithinImage(
                region,
                imageWidth: 2048,
                imageHeight: 945);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*outside the source image bounds*");
    }

    [Fact]
    public void ValidateWithinImage_WhenRegionStartsBeforeImage_ThrowsExpectedException()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            -1,
            100,
            100,
            100);

        // Act
        Action act = () =>
            OcrRegionGeometry.ValidateWithinImage(
                region,
                imageWidth: 2048,
                imageHeight: 945);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*outside the source image bounds*");
    }

    [Fact]
    public void ValidateWithinImage_WhenRegionHasZeroWidth_ThrowsExpectedException()
    {
        // Arrange
        Rectangle region = Rectangle.FromXYWH(
            100,
            100,
            0,
            100);

        // Act
        Action act = () =>
            OcrRegionGeometry.ValidateWithinImage(
                region,
                imageWidth: 2048,
                imageHeight: 945);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*positive width and height*");
    }
}