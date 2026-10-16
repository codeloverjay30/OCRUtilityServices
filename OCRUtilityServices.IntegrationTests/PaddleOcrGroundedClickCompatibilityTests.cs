using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class PaddleOcrGroundedClickCompatibilityTests
{
    private readonly OcrTextMatcher _matcher = new();

    [Theory]
    [InlineData(OcrTextMatchMode.Exact)]
    [InlineData(OcrTextMatchMode.NormalizedContains)]
    public void FindUniqueMatch_ExactLineWithoutWords_ShouldUseLineBounds(OcrTextMatchMode mode)
    {
        var bounds = Rectangle.FromXYWH(800, 700, 200, 40);
        var result = Result(new OcrTextLine("登 入 遊 戲", bounds));

        // Exact mode intentionally requires literal text equality.
        if (mode == OcrTextMatchMode.Exact)
        {
            result = Result(new OcrTextLine("登入遊戲", bounds));
        }

        OcrTextMatch match = _matcher.FindUniqueMatch(result, "登入遊戲", mode);

        match.Bounds.Should().Be(bounds);
        match.Text.Should().Be("登入遊戲");
    }

    [Fact]
    public void FindUniqueMatch_SubstringWithoutWords_ShouldFailClosed()
    {
        var result = Result(new OcrTextLine(
            "立即登入遊戲", Rectangle.FromXYWH(800, 700, 200, 40)));

        Action act = () => _matcher.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*word-level geometry is unavailable*");
    }

    [Fact]
    public void FindUniqueMatch_DuplicateExactLines_ShouldFailClosed()
    {
        var result = Result(
            new OcrTextLine("登入遊戲", Rectangle.FromXYWH(100, 100, 100, 30)),
            new OcrTextLine("登入遊戲", Rectangle.FromXYWH(400, 100, 100, 30)));

        Action act = () => _matcher.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ambiguous*multiple matching lines*");
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(100, 0)]
    [InlineData(-10, 30)]
    public void FindUniqueMatch_InvalidLineBounds_ShouldFailClosed(double widthOrX, double height)
    {
        var bounds = widthOrX < 0
            ? Rectangle.FromXYWH(widthOrX, 100, 100, height)
            : Rectangle.FromXYWH(100, 100, widthOrX, height);
        var result = Result(new OcrTextLine("登入遊戲", bounds));

        Action act = () => _matcher.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid line-level geometry*");
    }

    [Fact]
    public void FindUniqueMatch_WithWords_ShouldPreserveWordBounds()
    {
        var line = new OcrTextLine(
            "請登入遊戲",
            Rectangle.FromXYWH(100, 100, 200, 40),
            new[]
            {
                new OcrTextWord("請", Rectangle.FromXYWH(100, 100, 30, 40)),
                new OcrTextWord("登入遊戲", Rectangle.FromXYWH(135, 100, 150, 40))
            });

        OcrTextMatch match = _matcher.FindUniqueMatch(
            Result(line), "登入遊戲", OcrTextMatchMode.NormalizedContains);

        match.Bounds.Should().Be(Rectangle.FromXYWH(135, 100, 150, 40));
    }

    private static OcrResult Result(params OcrTextLine[] lines)
        => new(string.Join(Environment.NewLine, lines.Select(line => line.Text)), lines);
}
