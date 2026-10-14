using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;

namespace OCRUtilityServices.Tests;

public sealed class OcrTextMatcherCandidateTests
{
    private readonly OcrTextMatcher _sut = new();

    [Fact]
    public void FindUniqueMatch_WhenWhitespaceSeparatesWords_ReturnsUnionBounds()
    {
        OcrResult result = CreateResult(
            new OcrTextLine(
                "登 入 遊 戲",
                Rectangle.FromXYWH(10, 20, 120, 30),
                new[]
                {
                    new OcrTextWord("登", Rectangle.FromXYWH(10, 20, 20, 30)),
                    new OcrTextWord("入", Rectangle.FromXYWH(35, 20, 20, 30)),
                    new OcrTextWord("遊", Rectangle.FromXYWH(60, 20, 20, 30)),
                    new OcrTextWord("戲", Rectangle.FromXYWH(85, 20, 20, 30))
                }));

        OcrTextMatch match = _sut.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        match.Bounds.Should().Be(Rectangle.FromXYWH(10, 20, 95, 30));
    }

    [Fact]
    public void FindUniqueMatch_WhenTwoLinesMatch_RejectsAmbiguity()
    {
        OcrResult result = CreateResult(
            CreateLine("登入遊戲", 10),
            CreateLine("登入遊戲", 80));

        Action act = () => _sut.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ambiguous*multiple matching lines*");
    }

    [Fact]
    public void FindUniqueMatch_WhenSubstringHasNoWordGeometry_RejectsMatch()
    {
        OcrResult result = CreateResult(
            new OcrTextLine(
                "請登入遊戲以繼續",
                Rectangle.FromXYWH(10, 20, 200, 40)));

        Action act = () => _sut.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*word-level geometry is unavailable*");
    }

    [Fact]
    public void FindUniqueMatch_WhenTargetIsAbsent_ReportsNotFound()
    {
        OcrResult result = CreateResult(CreateLine("開始遊戲", 10));

        Action act = () => _sut.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*登入遊戲*was not found*");
    }

    [Fact]
    public void FindUniqueMatch_WhenSameLineContainsTwoTargetWordRanges_RejectsAmbiguity()
    {
        OcrResult result = CreateResult(
            new OcrTextLine(
                "登入遊戲 登入遊戲",
                Rectangle.FromXYWH(10, 20, 200, 30),
                new[]
                {
                    new OcrTextWord("登入遊戲", Rectangle.FromXYWH(10, 20, 80, 30)),
                    new OcrTextWord("登入遊戲", Rectangle.FromXYWH(110, 20, 80, 30))
                }));

        Action act = () => _sut.FindUniqueMatch(
            result, "登入遊戲", OcrTextMatchMode.NormalizedContains);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ambiguous*multiple word-level matches*");
    }

    private static OcrResult CreateResult(params OcrTextLine[] lines) =>
        new(string.Join(" ", lines.Select(line => line.Text)), lines);

    private static OcrTextLine CreateLine(string text, double y) =>
        new(
            text,
            Rectangle.FromXYWH(10, y, 120, 30),
            new[] { new OcrTextWord(text, Rectangle.FromXYWH(10, y, 120, 30)) });
}
