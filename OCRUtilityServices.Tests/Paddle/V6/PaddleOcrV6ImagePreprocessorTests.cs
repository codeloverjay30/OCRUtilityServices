using FluentAssertions;
using OCRUtilityServices.Paddle.V6;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OCRUtilityServices.Tests.Paddle.V6;

public sealed class PaddleOcrV6ImagePreprocessorTests
{
    private readonly PaddleOcrV6ImagePreprocessor _sut = new();

    [Fact]
    public void Preprocess_ConstantColorImage_ProducesBgrNormalizedTensorAndZeroPadding()
    {
        using var image = new Image<Rgb24>(24, 48, new Rgb24(255, 128, 0));
        var tensor = _sut.Preprocess(image);
        tensor.Dimensions.ToArray().Should().Equal(1, 3, 48, 320);
        tensor[0, 0, 20, 10].Should().BeApproximately(-1f, 0.0001f);
        tensor[0, 1, 20, 10].Should().BeApproximately(128f / 127.5f - 1f, 0.0001f);
        tensor[0, 2, 20, 10].Should().BeApproximately(1f, 0.0001f);
        tensor[0, 0, 20, 24].Should().Be(0f);
        tensor[0, 1, 20, 319].Should().Be(0f);
        tensor[0, 2, 20, 319].Should().Be(0f);
    }

    [Fact]
    public void Preprocess_WideImage_ClampsContentWidthTo320()
    {
        using var image = new Image<Rgb24>(640, 48, new Rgb24(255, 255, 255));
        var tensor = _sut.Preprocess(image);
        tensor.Dimensions.ToArray().Should().Equal(1, 3, 48, 320);
        tensor[0, 0, 10, 319].Should().BeApproximately(1f, 0.0001f);
    }

    [Fact]
    public void Preprocess_NullImage_Throws()
    {
        Action act = () => _sut.Preprocess(null!);
        act.Should().Throw<ArgumentNullException>().WithMessage("*image*");
    }

    [Fact]
    public void Preprocess_PreCancelled_Throws()
    {
        using var image = new Image<Rgb24>(1, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action act = () => _sut.Preprocess(image, cancellation.Token);
        act.Should().Throw<OperationCanceledException>();
    }
}
