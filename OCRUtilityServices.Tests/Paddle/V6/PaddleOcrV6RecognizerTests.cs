using FluentAssertions;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;
using OCRUtilityServices.Paddle.V6;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OCRUtilityServices.Tests.Paddle.V6;

public sealed class PaddleOcrV6RecognizerTests
{
    [Theory]
    [InlineData(0, "preprocessor")]
    [InlineData(1, "inferenceEngine")]
    [InlineData(2, "decoder")]
    public void Constructor_NullDependency_Throws(int missing, string parameter)
    {
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        Action act = () => new PaddleOcrV6Recognizer(
            missing == 0 ? null! : preprocessor.Object,
            missing == 1 ? null! : engine.Object,
            missing == 2 ? null! : decoder);
        act.Should().Throw<ArgumentNullException>().WithMessage($"*{parameter}*");
    }

    [Fact]
    public void Recognize_ValidImage_DelegatesInOrderAndReturnsDecodedResult()
    {
        using var image = new Image<Rgb24>(2, 2);
        var tensor = new DenseTensor<float>(new[] { 1, 3, 48, 320 });
        float[] values = [0.1f, 0.9f, 0.2f, 0.8f];
        var calls = new List<string>();
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        preprocessor.Setup(x => x.Preprocess(image, It.IsAny<CancellationToken>()))
            .Returns(() => { calls.Add("preprocess"); return tensor; });
        engine.Setup(x => x.Run(tensor, It.IsAny<CancellationToken>()))
            .Returns(() => { calls.Add("infer"); return (values, 2, 2); });
        decoder.OnDecode = (input, steps, classes) =>
        {
            input.ToArray().Should().Equal(values);
            steps.Should().Be(2);
            classes.Should().Be(2);
            calls.Add("decode");
            return ("登入遊戲", 0.99f);
        };

        using var sut = new PaddleOcrV6Recognizer(preprocessor.Object, engine.Object, decoder);
        var result = sut.Recognize(image);

        result.Text.Should().Be("登入遊戲");
        result.Score.Should().BeApproximately(0.99f, 0.00001f);
        calls.Should().Equal("preprocess", "infer", "decode");
        preprocessor.VerifyAll();
        engine.VerifyAll();
        decoder.CallCount.Should().Be(1);
    }

    [Fact]
    public void Recognize_NullImage_ThrowsWithoutInvokingDependencies()
    {
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        using var sut = new PaddleOcrV6Recognizer(preprocessor.Object, engine.Object, decoder);
        Action act = () => sut.Recognize(null!);
        act.Should().Throw<ArgumentNullException>().WithMessage("*image*");
        preprocessor.VerifyNoOtherCalls();
        engine.VerifyNoOtherCalls();
        decoder.CallCount.Should().Be(0);
    }

    [Fact]
    public void Recognize_PreCancelled_ThrowsWithoutInvokingDependencies()
    {
        using var image = new Image<Rgb24>(1, 1);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        using var sut = new PaddleOcrV6Recognizer(preprocessor.Object, engine.Object, decoder);
        Action act = () => sut.Recognize(image, source.Token);
        act.Should().Throw<OperationCanceledException>();
        preprocessor.VerifyNoOtherCalls();
        engine.VerifyNoOtherCalls();
        decoder.CallCount.Should().Be(0);
    }

    [Fact]
    public void Recognize_AfterDispose_ThrowsAndDoesNotDisposeInjectedEngine()
    {
        using var image = new Image<Rgb24>(1, 1);
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        var sut = new PaddleOcrV6Recognizer(preprocessor.Object, engine.Object, decoder);
        sut.Dispose();
        sut.Dispose();
        Action act = () => sut.Recognize(image);
        act.Should().Throw<ObjectDisposedException>().WithMessage("*PaddleOcrV6Recognizer*");
        engine.Verify(x => x.Dispose(), Times.Never);
        preprocessor.VerifyNoOtherCalls();
        engine.VerifyNoOtherCalls();
        decoder.CallCount.Should().Be(0);
    }

    [Fact]
    public void Recognize_InferenceFailure_PropagatesActualExceptionWithoutDecoding()
    {
        using var image = new Image<Rgb24>(1, 1);
        var tensor = new DenseTensor<float>(new[] { 1, 3, 48, 320 });
        var preprocessor = new Mock<IPaddleOcrV6ImagePreprocessor>(MockBehavior.Strict);
        var engine = new Mock<IPaddleOcrV6InferenceEngine>(MockBehavior.Strict);
        var decoder = new StubDecoder();
        preprocessor.Setup(x => x.Preprocess(image, It.IsAny<CancellationToken>())).Returns(tensor);
        engine.Setup(x => x.Run(tensor, It.IsAny<CancellationToken>()))
            .Throws(new InvalidDataException("ONNX inference failed."));
        using var sut = new PaddleOcrV6Recognizer(preprocessor.Object, engine.Object, decoder);
        Action act = () => sut.Recognize(image);
        act.Should().Throw<InvalidDataException>().WithMessage("*ONNX inference failed.*");
        decoder.CallCount.Should().Be(0);
    }

    private sealed class StubDecoder : IPaddleOcrV6CtcDecoder
    {
        public int CallCount { get; private set; }
        public Func<float[], int, int, (string Text, float Score)>? OnDecode { get; set; }

        public (string Text, float Score) Decode(ReadOnlySpan<float> probabilities, int steps, int classes)
        {
            CallCount++;
            return OnDecode is null ? (string.Empty, 0f) : OnDecode(probabilities.ToArray(), steps, classes);
        }
    }
}
