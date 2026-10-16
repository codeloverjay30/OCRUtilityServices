using System.IO.Abstractions;
using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Paddle.V6;
using Xunit;

namespace OCRUtilityServices.Tests.Paddle.V6;

public sealed class PaddleOcrV6CtcDecoderTests : IDisposable
{
    private const int Classes = 18710;
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly string _directory;

    public PaddleOcrV6CtcDecoderTests()
    {
        _directory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(), "PaddleOcrV6DecoderTests-" + Guid.NewGuid().ToString("N"));
        _fileSystem.Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Decode_RepeatedCharactersAndBlank_UsesCtcRulesAndMeanConfidence()
    {
        var sut = CreateDecoder();
        // blank, 登, 登, blank, 登, 入, 遊, 戲
        int[] indices = [0, 1, 1, 0, 1, 2, 3, 4];
        float[] values = Scores(indices, 0.9f);
        var result = sut.Decode(values, indices.Length, Classes);
        result.Text.Should().Be("登登入遊戲");
        result.Score.Should().BeApproximately(0.9f, 0.0001f);
    }

    [Fact]
    public void Decode_BlanksOnly_ReturnsEmptyTextAndZeroConfidence()
    {
        var sut = CreateDecoder();
        var result = sut.Decode(Scores([0, 0, 0], 0.9f), 3, Classes);
        result.Text.Should().BeEmpty();
        result.Score.Should().Be(0f);
    }

    [Fact]
    public void Decode_TrailingSpaceClass_IsRecognized()
    {
        var sut = CreateDecoder();
        var result = sut.Decode(Scores([1, Classes - 1, 2], 0.9f), 3, Classes);
        result.Text.Should().Be("登 入");
    }

    [Theory]
    [InlineData(0, Classes)]
    [InlineData(1, Classes - 1)]
    [InlineData(2, Classes)]
    public void Decode_InvalidDimensions_ThrowsWithActualMessage(int steps, int classes)
    {
        var sut = CreateDecoder();
        Action act = () => sut.Decode(new float[Classes], steps, classes);
        act.Should().Throw<InvalidDataException>().WithMessage("*Unexpected OCR output tensor shape*");
    }

    [Fact]
    public void Decode_NonFiniteValue_ThrowsWithActualMessage()
    {
        var sut = CreateDecoder();
        float[] values = Scores([1], 0.9f);
        values[10] = float.NaN;
        Action act = () => sut.Decode(values, 1, Classes);
        act.Should().Throw<InvalidDataException>().WithMessage("*non-finite values*");
    }

    [Fact]
    public void Constructor_InvalidDictionaryLength_Throws()
    {
        string path = _fileSystem.Path.Combine(_directory, "invalid.json");
        _fileSystem.File.WriteAllText(path, "[\"登\"]");
        Action act = () => new PaddleOcrV6CtcDecoder(path);
        act.Should().Throw<InvalidDataException>().WithMessage("*18708 string entries*");
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        Action act = () => new PaddleOcrV6CtcDecoder("");
        act.Should().Throw<ArgumentException>().WithMessage("*dictionaryPath*");
    }

    public void Dispose()
    {
        if (_fileSystem.Directory.Exists(_directory))
            _fileSystem.Directory.Delete(_directory, recursive: true);
    }

    private PaddleOcrV6CtcDecoder CreateDecoder()
    {
        string path = _fileSystem.Path.Combine(_directory, "character_dict.json");
        string[] chars = new string[18708];
        chars[0] = "登";
        chars[1] = "入";
        chars[2] = "遊";
        chars[3] = "戲";
        for (int i = 4; i < chars.Length; i++)
            chars[i] = $"token-{i}";
        _fileSystem.File.WriteAllText(path, JsonSerializer.Serialize(chars));
        return new PaddleOcrV6CtcDecoder(path);
    }

    private static float[] Scores(int[] indices, float peak)
    {
        var values = new float[checked(indices.Length * Classes)];
        for (int step = 0; step < indices.Length; step++)
            values[step * Classes + indices[step]] = peak;
        return values;
    }
}
