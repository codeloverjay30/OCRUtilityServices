using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;

namespace OCRUtilityServices.Tests.Services;

public sealed class OcrTextMatcherTests
{
    private readonly IOcrTextMatcher _sut = new OcrTextMatcher();

    [Fact]
    public void FindUnique_WhenExactlyOneLineMatches_ReturnsSameLine()
    {
        // Arrange
        OcrTextLine expected = CreateLine("任務");

        OcrResult result = CreateResult(
            CreateLine("商店"),
            expected,
            CreateLine("背包"));

        // Act
        OcrTextLine actual = _sut.FindUnique(result, "任務");

        // Assert
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void FindUnique_WhenTextHasSurroundingWhitespace_ReturnsMatchingLine()
    {
        // Arrange
        OcrTextLine expected = CreateLine("  任務 \t");

        OcrResult result = CreateResult(expected);

        // Act
        OcrTextLine actual = _sut.FindUnique(result, "\t 任務  ");

        // Assert
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void FindUnique_WhenTargetIsMissing_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("商店"),
            CreateLine("背包"));

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OCR target '任務' was not found.");
    }

    [Fact]
    public void FindUnique_WhenResultContainsNoLines_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult();

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OCR target '任務' was not found.");
    }

    [Fact]
    public void FindUnique_WhenMultipleLinesMatch_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("任務"),
            CreateLine("商店"),
            CreateLine(" 任務 "));

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "OCR target '任務' is ambiguous: multiple matching lines were found.");
    }

    [Fact]
    public void FindUnique_WhenResultIsNull_ThrowsExpectedException()
    {
        // Act
        Action act = () => _sut.FindUnique(null!, "任務");

        // Assert
        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*Value cannot be null*result*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void FindUnique_WhenTargetIsNullOrWhitespace_ThrowsExpectedException(
        string? targetText)
    {
        // Arrange
        OcrResult result = CreateResult();

        // Act
        Action act = () => _sut.FindUnique(result, targetText!);

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*targetText*");
    }

    [Fact]
    public void FindUnique_WhenLineCollectionIsNull_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = new(
            string.Empty,
            null!);

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "The OCR result must contain a non-null line collection.*");
    }

    [Fact]
    public void FindUnique_WhenLineCollectionContainsNull_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("商店"),
            null!);

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "The OCR result contains a null text line.*");
    }

    [Fact]
    public void FindUnique_WhenTextDiffersByCase_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("VIP"));

        // Act
        Action act = () => _sut.FindUnique(result, "vip");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OCR target 'vip' was not found.");
    }

    [Fact]
    public void FindUnique_WhenTargetIsOnlyPartOfLine_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("每日任務"));

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OCR target '任務' was not found.");
    }

    [Fact]
    public void FindUnique_WhenTextContainsInternalWhitespace_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("任 務"));

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OCR target '任務' was not found.");
    }

    [Fact]
    public void FindUnique_WhenMatchingLinePrecedesInvalidLine_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("任務"),
            null!);

        // Act
        Action act = () => _sut.FindUnique(result, "任務");

        // Assert
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "The OCR result contains a null text line.*");
    }

    [Fact]
    public void FindUnique_WhenMatchedTextContainsInterCharacterWhitespace_ShouldReturnLine()
    {
        OcrTextLine expectedLine =
            CreateLine("任 務");

        OcrResult result =
            CreateResult(expectedLine);

        var sut = new OcrTextMatcher();

        OcrTextLine actual =
            sut.FindUnique(
                result,
                "任務",
            OcrTextMatchMode.NormalizedContains);


        actual.Should()
            .BeSameAs(expectedLine);
    }

    [Fact]
    public void FindUnique_WhenTargetIsContainedInNoisyOcrLine_ShouldReturnLine()
    {
        OcrTextLine expectedLine =
            CreateLine("冖 | 一 卜 任 務");

        OcrResult result =
            CreateResult(expectedLine);

        var sut = new OcrTextMatcher();

        OcrTextLine actual =
            sut.FindUnique(
                result,
                "任務",
            OcrTextMatchMode.NormalizedContains);

        actual.Should()
            .BeSameAs(expectedLine);
    }

    [Fact]
    public void FindUnique_WhenNormalizedContainsMatchesMultipleLines_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("每日 任 務"),
            CreateLine("主線 任 務"));

        // Act
        Action act = () => _sut.FindUnique(
            result,
            "任務",
            OcrTextMatchMode.NormalizedContains);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "OCR target '任務' is ambiguous: multiple matching lines were found.");
    }


    [Fact]
    public void FindUnique_WhenMatchModeIsUnsupported_ThrowsExpectedException()
    {
        // Arrange
        OcrResult result = CreateResult(
            CreateLine("任務"));

        OcrTextMatchMode unsupportedMatchMode =
            (OcrTextMatchMode)999;

        // Act
        Action act = () => _sut.FindUnique(
            result,
            "任務",
            unsupportedMatchMode);

        // Assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage(
                "*The specified OCR text match mode is not supported.*matchMode*");
    }

    [Fact]
    public void FindUniqueMatch_WhenTargetSpansMultipleWords_ShouldReturnUnionOfTargetWordBounds()
    {
        // Arrange
        const string targetText = "任務";

        var line = new OcrTextLine(
            "掛 收 益 一 ∕ 丶 任 務 一 三 國 巔 峰 戰",
            Rectangle.FromXYWH(
                100,
                900,
                565,
                54),
            [
                new OcrTextWord(
                "掛",
                Rectangle.FromXYWH(100, 910, 40, 32)),
            new OcrTextWord(
                "收",
                Rectangle.FromXYWH(150, 910, 40, 32)),
            new OcrTextWord(
                "益",
                Rectangle.FromXYWH(200, 910, 40, 32)),
            new OcrTextWord(
                "任",
                Rectangle.FromXYWH(530, 910, 40, 32)),
            new OcrTextWord(
                "務",
                Rectangle.FromXYWH(580, 910, 40, 32)),
            new OcrTextWord(
                "三",
                Rectangle.FromXYWH(625, 910, 40, 32))
            ]);

        var result = new OcrResult(
            line.Text,
            [line]);

        var sut = new OcrTextMatcher();

        // Act
        OcrTextMatch match =
            sut.FindUniqueMatch(
                result,
                targetText,
                OcrTextMatchMode.NormalizedContains);

        // Assert
        match.Text.Should().Be(targetText);

        match.Bounds.TopLeft.X.Should().Be(530);
        match.Bounds.TopLeft.Y.Should().Be(910);

        match.Bounds.BottomRight.X.Should().Be(620);
        match.Bounds.BottomRight.Y.Should().Be(942);

        match.Bounds.Width.Should().Be(90);
        match.Bounds.Height.Should().Be(32);

        match.Bounds.Center.X.Should().Be(575);
        match.Bounds.Center.Y.Should().Be(926);
    }

    private static OcrResult CreateResult(
        params OcrTextLine[] lines)
    {
        return new OcrResult(
            string.Empty,
            lines);
    }

    private static OcrTextLine CreateLine(string text)
    {
        return new OcrTextLine(
            text,
            Rectangle.FromXYWH(
                10,
                20,
                100,
                40));
    }
}