using System.Security.Cryptography;
using CoordinateUtilityServices;
using FluentAssertions;
using Moq;
using OCRUtilityServices.AnnotationTool.Services;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle.V6;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Rectangle = CoordinateUtilityServices.Rectangle;

namespace OCRUtilityServices.IntegrationTests;

public sealed class V3V6CandidateGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_OneExactMatch_ShouldRetainBothBoundsAndRemainPendingReview()
    {
        byte[] png = CreatePng();
        var detector = new Mock<IOCRUtilityService>(MockBehavior.Strict);
        detector.Setup(x => x.RecognizeRegionAsync(
                It.IsAny<byte[]>(), It.IsAny<Rectangle>(), "zh-TW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult("二二》", new[]
            {
                new OcrTextLine("二二》", Rectangle.FromXYWH(56, 17, 51, 18))
            }));
        var recognizer = new Mock<IPaddleOcrV6Recognizer>(MockBehavior.Strict);
        recognizer.Setup(x => x.Recognize(It.IsAny<Image<Rgb24>>(), It.IsAny<CancellationToken>()))
            .Returns(("任務", 0.999835491f));

        var sut = new V3V6CandidateGenerator(detector.Object, recognizer.Object);
        V3V6CandidateReport report = await sut.GenerateAsync(
            png, "screen.png", "任務", Rectangle.FromXYWH(40, 5, 100, 50));

        report.Status.Should().Be("PendingReview");
        report.Candidates.Should().ContainSingle();
        V3V6Candidate candidate = report.Candidates.Single();
        candidate.RawText.Should().Be("任務");
        candidate.Confidence.Should().BeApproximately(0.999835491f, 0.000001f);
        candidate.DetectionBounds.Should().Be(new CandidateBounds(56, 17, 51, 18));
        candidate.RecognitionCropBounds.Should().Be(new CandidateBounds(48, 9, 67, 34));
        candidate.CropPadding.Should().Be(8);
        report.ScreenshotSha256.Should().Be(
            Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());
        V3V6CandidateGenerator.Serialize(report).Should().Contain("\"PendingReview\"");
    }

    [Fact]
    public async Task GenerateAsync_NonmatchingText_ShouldReturnNoCandidates()
    {
        byte[] png = CreatePng();
        var detector = new Mock<IOCRUtilityService>(MockBehavior.Strict);
        detector.Setup(x => x.RecognizeRegionAsync(
                It.IsAny<byte[]>(), It.IsAny<Rectangle>(), "zh-TW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult("二二》", new[]
            {
                new OcrTextLine("二二》", Rectangle.FromXYWH(56, 17, 51, 18))
            }));
        var recognizer = new Mock<IPaddleOcrV6Recognizer>(MockBehavior.Strict);
        recognizer.Setup(x => x.Recognize(It.IsAny<Image<Rgb24>>(), It.IsAny<CancellationToken>()))
            .Returns(("三國真", 0.99f));

        var sut = new V3V6CandidateGenerator(detector.Object, recognizer.Object);
        V3V6CandidateReport report = await sut.GenerateAsync(
            png, "screen.png", "任務", Rectangle.FromXYWH(40, 5, 100, 50));

        report.Candidates.Should().BeEmpty();
        report.Status.Should().Be("PendingReview");
    }

    [Fact]
    public async Task GenerateAsync_InvalidGeometry_ShouldFailClosedBeforeRecognition()
    {
        var detector = new Mock<IOCRUtilityService>(MockBehavior.Strict);
        detector.Setup(x => x.RecognizeRegionAsync(
                It.IsAny<byte[]>(), It.IsAny<Rectangle>(), "zh-TW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult("invalid", new[]
            {
                new OcrTextLine("invalid", Rectangle.FromXYWH(190, 17, 51, 18))
            }));
        var recognizer = new Mock<IPaddleOcrV6Recognizer>(MockBehavior.Strict);
        var sut = new V3V6CandidateGenerator(detector.Object, recognizer.Object);

        Func<Task> act = () => sut.GenerateAsync(
            CreatePng(), "screen.png", "任務", Rectangle.FromXYWH(40, 5, 100, 50));

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*invalid source-image geometry*");
        recognizer.Verify(x => x.Recognize(
            It.IsAny<Image<Rgb24>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_DuplicateMatches_ShouldPreserveAmbiguity()
    {
        var detector = new Mock<IOCRUtilityService>(MockBehavior.Strict);
        detector.Setup(x => x.RecognizeRegionAsync(
                It.IsAny<byte[]>(), It.IsAny<Rectangle>(), "zh-TW", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrResult("two", new[]
            {
                new OcrTextLine("one", Rectangle.FromXYWH(50, 15, 30, 15)),
                new OcrTextLine("two", Rectangle.FromXYWH(100, 15, 30, 15))
            }));
        var recognizer = new Mock<IPaddleOcrV6Recognizer>(MockBehavior.Strict);
        recognizer.Setup(x => x.Recognize(It.IsAny<Image<Rgb24>>(), It.IsAny<CancellationToken>()))
            .Returns(("任務", 0.999f));
        var sut = new V3V6CandidateGenerator(detector.Object, recognizer.Object);

        V3V6CandidateReport report = await sut.GenerateAsync(
            CreatePng(), "screen.png", "任務", Rectangle.FromXYWH(40, 5, 100, 50));

        report.Candidates.Should().HaveCount(2);
        report.Status.Should().Be("PendingReview");
    }

    [Fact]
    public void Serialize_ReviewedStatus_ShouldRejectApprovalEscalation()
    {
        var report = new V3V6CandidateReport(
            "1.0", "test", "screen.png", new string('a', 64), "任務",
            "Reviewed", true, Array.Empty<V3V6Candidate>());

        Action act = () => V3V6CandidateGenerator.Serialize(report);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must remain PendingReview*");
    }

    private static byte[] CreatePng()
    {
        using var image = new Image<Rgb24>(200, 100);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
