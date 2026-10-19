using System.IO.Abstractions;
using System.Text.Json;
using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48ReviewedGroundTruthIntegrationTests
{
    private const string ScreenshotDigest =
        "1b5739e711ff97e211326cb3d6009e2e1a19421d8bea529a922e0b993ea0e34d";
    private const string AnnotationDigest =
        "3ffa69777c017082ce3b79295215bba4efb4345366c870e9c5c62146129ebc51";
    private readonly IFileSystem _fileSystem = new FileSystem();

    [Fact]
    public async Task LoadAsync_ReviewedIssue48Fixture_ShouldRetainApprovedGeometry()
    {
        ReviewedGroundTruth truth = await LoadReviewedAsync();

        truth.Fixture.Should().Be("task-home-original.png");
        truth.TargetText.Should().Be("任務");
        truth.ImageWidth.Should().Be(2340);
        truth.ImageHeight.Should().Be(1080);
        truth.TextBounds.Should().Be(Rectangle.FromXYWH(556, 917, 51, 18));
        truth.ButtonBounds.Should().Be(Rectangle.FromXYWH(515, 815, 135, 130));
    }

    [Fact]
    public async Task Authorize_ReviewedIssue48V8Candidate_ShouldReturnPointWithoutClick()
    {
        ReviewedGroundTruth truth = await LoadReviewedAsync();
        string candidatePath = FixturePath("Reviews","task-home.ocr-candidates.v8.json");
        byte[] bytes = await _fileSystem.File.ReadAllBytesAsync(candidatePath);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;

        root.GetProperty("Status").GetString().Should().Be("PendingReview");
        root.GetProperty("ScreenshotSha256").GetString().Should().Be(ScreenshotDigest);
        root.GetProperty("ExpectedText").GetString().Should().Be("任務");

        JsonElement.ArrayEnumerator entries = root.GetProperty("Candidates").EnumerateArray();
        JsonElement[] candidates = entries.ToArray();
        candidates.Should().ContainSingle();
        JsonElement candidate = candidates.Single();
        candidate.GetProperty("RawText").GetString().Should().Be("任務");
        candidate.GetProperty("CropPadding").GetInt32().Should().Be(8);

        JsonElement detection = candidate.GetProperty("DetectionBounds");
        Rectangle detected = Rectangle.FromXYWH(
            detection.GetProperty("X").GetDouble(),
            detection.GetProperty("Y").GetDouble(),
            detection.GetProperty("Width").GetDouble(),
            detection.GetProperty("Height").GetDouble());

        detected.Should().Be(truth.TextBounds);
        float confidence = candidate.GetProperty("Confidence").GetSingle();

        IGroundedClickAuthorizationService service =
            new GroundedClickAuthorizationService(new GroundedClickValidator());

        // Pure policy evaluation only: no Appium driver, tap, or click is created.
        (double X, double Y) point = service.Authorize(
            candidate.GetProperty("RawText").GetString()!,
            confidence,
            detected,
            truth);

        point.X.Should().Be(581.5);
        point.Y.Should().Be(926);
    }

    [Fact]
    public async Task LoadAsync_TamperedScreenshot_ShouldFailClosedWithDigestMessage()
    {
        string screenshot = FixturePath("Screenshots","task-home-original.png");
        string annotation = FixturePath("GroundTruth","task-home.ground-truth.reviewed.json");
        byte[] bytes = await _fileSystem.File.ReadAllBytesAsync(screenshot);
        bytes[0] ^= 0x01;

        string temporaryDirectory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(), "issue48-" + Guid.NewGuid().ToString("N"));
        _fileSystem.Directory.CreateDirectory(temporaryDirectory);
        string tamperedPath = _fileSystem.Path.Combine(
            temporaryDirectory, "task-home-original.png");
        try
        {
            await _fileSystem.File.WriteAllBytesAsync(tamperedPath, bytes);
            var loader = new GroundTruthLoader(_fileSystem);
            Func<Task> act = () => loader.LoadAsync(
                annotation, tamperedPath, AnnotationDigest, ScreenshotDigest);

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*Screenshot SHA-256 does not match*");
        }
        finally
        {
            if (_fileSystem.Directory.Exists(temporaryDirectory))
                _fileSystem.Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task<ReviewedGroundTruth> LoadReviewedAsync()
    {
        var loader = new GroundTruthLoader(_fileSystem);
        return await loader.LoadAsync(
            FixturePath("GroundTruth","task-home.ground-truth.reviewed.json"),
            FixturePath("Screenshots","task-home-original.png"),
            AnnotationDigest,
            ScreenshotDigest);
    }

    private static string FixturePath(string folderPath1,string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Issue48", folderPath1,fileName);
}
