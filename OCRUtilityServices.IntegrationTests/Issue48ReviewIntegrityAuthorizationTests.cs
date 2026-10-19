using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoordinateUtilityServices;
using FluentAssertions;
using Moq;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48ReviewIntegrityAuthorizationTests : IDisposable
{
    private readonly IFileSystem _fs = new FileSystem();
    private readonly string _directory;

    public Issue48ReviewIntegrityAuthorizationTests()
    {
        _directory = _fs.Path.Combine(_fs.Path.GetTempPath(),
            "issue48-integrity-" + Guid.NewGuid().ToString("N"));
        _fs.Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("annotation")]
    [InlineData("screenshot")]
    public async Task AuthorizeAsync_MismatchedTrustedEvidence_ShouldFailClosed(string changed)
    {
        Fixture fixture = await CreateFixtureAsync();
        var loader = new Mock<IGroundTruthLoader>(MockBehavior.Strict);
        var gate = new Mock<IReviewAwareGroundedClickAuthorizationService>(MockBehavior.Strict);
        var sut = new ReviewIntegrityAuthorizationService(_fs, loader.Object, gate.Object);

        string reviewHash = changed == "review" ? new string('0', 64) : fixture.ReviewHash;
        string annotationHash = changed == "annotation" ? new string('0', 64) : fixture.AnnotationHash;
        string screenshotHash = changed == "screenshot" ? new string('0', 64) : fixture.ScreenshotHash;

        Func<Task> act = () => sut.AuthorizeAsync("任務", 0.999f,
            Rectangle.FromXYWH(556, 917, 51, 18),
            fixture.ReviewPath, fixture.AnnotationPath, fixture.ScreenshotPath,
            reviewHash, annotationHash, screenshotHash);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(changed switch
            {
                "review" => "*Review SHA-256 does not match trusted digest*",
                "annotation" => "*Review AnnotationSha256 does not match trusted evidence*",
                _ => "*Review ScreenshotSha256 does not match trusted evidence*"
            });
        loader.VerifyNoOtherCalls();
        gate.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthorizeAsync_ValidReviewDigestButPending_ShouldFailClosed()
    {
        Fixture fixture = await CreateFixtureAsync();
        ReviewedGroundTruth truth = CreateTruth();
        var loader = new Mock<IGroundTruthLoader>(MockBehavior.Strict);
        loader.Setup(x => x.LoadAsync(
                fixture.AnnotationPath, fixture.ScreenshotPath,
                fixture.AnnotationHash, fixture.ScreenshotHash,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(truth);
        var gate = new ReviewAwareGroundedClickAuthorizationService(
            new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict).Object);
        var sut = new ReviewIntegrityAuthorizationService(_fs, loader.Object, gate);

        Func<Task> act = () => sut.AuthorizeAsync("任務", 0.999f,
            Rectangle.FromXYWH(556, 917, 51, 18),
            fixture.ReviewPath, fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.ReviewHash, fixture.AnnotationHash, fixture.ScreenshotHash);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Review AnnotationDigestVerified must be true*");
        loader.Verify(x => x.LoadAsync(
            fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.AnnotationHash, fixture.ScreenshotHash,
            It.IsAny<CancellationToken>()), Times.Once);
        loader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthorizeAsync_ApprovedSyntheticEvidence_ShouldDelegateWithoutDeviceInput()
    {
        Fixture fixture = await CreateFixtureAsync(approved: true);
        ReviewedGroundTruth truth = CreateTruth();
        Rectangle detected = Rectangle.FromXYWH(556, 917, 51, 18);
        var loader = new Mock<IGroundTruthLoader>(MockBehavior.Strict);
        loader.Setup(x => x.LoadAsync(
            fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.AnnotationHash, fixture.ScreenshotHash,
            It.IsAny<CancellationToken>())).ReturnsAsync(truth);
        var gate = new Mock<IReviewAwareGroundedClickAuthorizationService>(MockBehavior.Strict);
        gate.Setup(x => x.Authorize("任務", 0.999f, detected, truth, It.IsAny<JsonElement>()))
            .Returns((581.5, 926d));
        var sut = new ReviewIntegrityAuthorizationService(_fs, loader.Object, gate.Object);

        var point = await sut.AuthorizeAsync("任務", 0.999f, detected,
            fixture.ReviewPath, fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.ReviewHash, fixture.AnnotationHash, fixture.ScreenshotHash);

        point.X.Should().Be(581.5);
        point.Y.Should().Be(926);
        
        gate.Verify(
            x => x.Authorize(
                "任務",
                0.999f,
                detected,
                truth,
                It.IsAny<JsonElement>()),
            Times.Once);

        loader.Verify(
            x => x.LoadAsync(
                fixture.AnnotationPath,
                fixture.ScreenshotPath,
                fixture.AnnotationHash,
                fixture.ScreenshotHash,
                It.IsAny<CancellationToken>()),
            Times.Once);

        gate.VerifyNoOtherCalls();
        loader.VerifyNoOtherCalls();

    }

    public void Dispose()
    {
        if (_fs.Directory.Exists(_directory))
            _fs.Directory.Delete(_directory, recursive: true);
    }

    private async Task<Fixture> CreateFixtureAsync(bool approved = false)
    {
        string annotationPath = _fs.Path.Combine(_directory, "task-home.ground-truth.reviewed.json");
        string screenshotPath = _fs.Path.Combine(_directory, "task-home-original.png");
        string reviewPath = _fs.Path.Combine(_directory, "task-home.review.json");
        byte[] annotation = Encoding.UTF8.GetBytes("synthetic annotation");
        byte[] screenshot = Encoding.UTF8.GetBytes("synthetic screenshot");
        await _fs.File.WriteAllBytesAsync(annotationPath, annotation);
        await _fs.File.WriteAllBytesAsync(screenshotPath, screenshot);
        string annotationHash = Hash(annotation);
        string screenshotHash = Hash(screenshot);
        var review = new
        {
            AnnotationFile = _fs.Path.GetFileName(annotationPath),
            AnnotationSha256 = annotationHash,
            ScreenshotSha256 = screenshotHash,
            Decision = new {
                Status = approved ? "Approved" : "PendingGeometryReview",
                GroundTruthApproved = approved, ClickAuthorized = approved
            },
            Integrity = new {
                AnnotationDigestVerified = approved,
                ScreenshotDigestVerified = approved,
                ScreenshotDimensionsVerified = approved
            },
            GeometryReview = new {
                TextBoundsConfirmed = approved,
                ButtonBoundsConfirmed = approved,
                SafeClickAreaConfirmed = approved
            }
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(review);
        await _fs.File.WriteAllBytesAsync(reviewPath, bytes);
        return new Fixture(reviewPath, annotationPath, screenshotPath,
            Hash(bytes), annotationHash, screenshotHash);
    }

    private static ReviewedGroundTruth CreateTruth() => new(
        "task-home-original.png", "任務",
        Rectangle.FromXYWH(556, 917, 51, 18),
        Rectangle.FromXYWH(515, 815, 135, 130), 2340, 1080);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Fixture(string ReviewPath, string AnnotationPath,
        string ScreenshotPath, string ReviewHash, string AnnotationHash, string ScreenshotHash);
}
