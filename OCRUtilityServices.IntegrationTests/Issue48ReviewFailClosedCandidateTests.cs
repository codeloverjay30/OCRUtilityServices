using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48ReviewFailClosedCandidateTests
{
    private const string ApprovedScreenshotSha256 =
        "1b5739e711ff97e211326cb3d6009e2e1a19421d8bea529a922e0b993ea0e34d";

    private const string LegacyMismatchedAnnotationSha256 =
        "3ffa69777c017082ce3b79295215bba4efb4345366c870e9c5c62146129ebc51";

    private readonly IFileSystem _fileSystem = new FileSystem();

    [Fact]
    public async Task LoadAsync_AnnotationNotMatchingReviewPin_ShouldFailClosed()
    {
        string annotationPath = FixturePath("GroundTruth", "task-home.ground-truth.reviewed.json");
        string screenshotPath = FixturePath("Screenshots", "task-home-original.png");
        var loader = new GroundTruthLoader(_fileSystem);

        Func<Task> act = () => loader.LoadAsync(
            annotationPath, screenshotPath,
            LegacyMismatchedAnnotationSha256, ApprovedScreenshotSha256);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*Annotation SHA-256 does not match approved digest*");
    }

    [Fact]
    public async Task ReviewFixture_PendingGeometryReview_ShouldNotClaimClickAuthorization()
    {
        using JsonDocument review = await ReadJsonAsync(
            FixturePath("Reviews", "task-home.review.json"));

        JsonElement root = review.RootElement;
        JsonElement decision = root.GetProperty("Decision");
        JsonElement integrity = root.GetProperty("Integrity");
        JsonElement geometry = root.GetProperty("GeometryReview");
        JsonElement execution = root.GetProperty("Execution");

        decision.GetProperty("Status").GetString().Should().Be("PendingGeometryReview");
        decision.GetProperty("GroundTruthApproved").GetBoolean().Should().BeFalse();
        decision.GetProperty("ClickAuthorized").GetBoolean().Should().BeFalse();
        execution.GetProperty("ClickAttempts").GetInt32().Should().Be(0);

        integrity.GetProperty("AnnotationDigestVerified").GetBoolean().Should().BeFalse();
        integrity.GetProperty("ScreenshotDigestVerified").GetBoolean().Should().BeFalse();
        integrity.GetProperty("ScreenshotDimensionsVerified").GetBoolean().Should().BeFalse();
        geometry.GetProperty("TextBoundsConfirmed").GetBoolean().Should().BeFalse();
        geometry.GetProperty("ButtonBoundsConfirmed").GetBoolean().Should().BeFalse();
        geometry.GetProperty("SafeClickAreaConfirmed").GetBoolean().Should().BeFalse();
    }


    [Fact]
    public async Task ReviewFixture_AnnotationReferenceAndDigest_ShouldMatchCurrentFixture()
    {
        string annotationPath = FixturePath(
            "GroundTruth",
            "task-home.ground-truth.reviewed.json");

        using JsonDocument review = await ReadJsonAsync(
            FixturePath("Reviews", "task-home.review.json"));

        JsonElement root = review.RootElement;
        JsonElement integrity = root.GetProperty("Integrity");
        JsonElement decision = root.GetProperty("Decision");

        string? referencedFile = root
            .GetProperty("AnnotationFile")
            .GetString();

        string? pinnedDigest = integrity
            .GetProperty("AnnotationSha256")
            .GetString();

        byte[] annotationBytes =
            await _fileSystem.File.ReadAllBytesAsync(annotationPath);

        string actualDigest = Convert.ToHexString(
            SHA256.HashData(annotationBytes));

        referencedFile.Should().Be(
            _fileSystem.Path.GetFileName(annotationPath));

        pinnedDigest.Should().NotBeNullOrWhiteSpace();

        pinnedDigest.Should().BeEquivalentTo(
            actualDigest,
            "the review must reference the exact annotation bytes");

        actualDigest.Should().Be(
            "F5CC6CB7BD3AA936D9C82A59D01F0BFB7DCFBB3E9EFC39E979ECA6BDCFEE0C14");

        decision.GetProperty("Status")
            .GetString()
            .Should().Be("PendingGeometryReview");

        decision.GetProperty("GroundTruthApproved")
            .GetBoolean()
            .Should().BeFalse();

        decision.GetProperty("ClickAuthorized")
            .GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public async Task ReviewFixture_ScreenshotPin_ShouldMatchCurrentFixture()
    {
        string screenshotPath = FixturePath("Screenshots", "task-home-original.png");
        using JsonDocument review = await ReadJsonAsync(
            FixturePath("Reviews", "task-home.review.json"));

        byte[] screenshotBytes = await _fileSystem.File.ReadAllBytesAsync(screenshotPath);
        string actualDigest = Convert.ToHexString(SHA256.HashData(screenshotBytes));
        string? pinnedDigest = review.RootElement.GetProperty("Integrity")
            .GetProperty("ScreenshotSha256").GetString();

        pinnedDigest.Should().NotBeNull();
        pinnedDigest!.ToLowerInvariant().Should().Be(ApprovedScreenshotSha256);
        actualDigest.ToLowerInvariant().Should().Be(ApprovedScreenshotSha256);
    }

    private async Task<JsonDocument> ReadJsonAsync(string path)
    {
        byte[] bytes = await _fileSystem.File.ReadAllBytesAsync(path);
        return JsonDocument.Parse(bytes);
    }

    private static string FixturePath(string directory, string filename) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Issue48", directory, filename);
}
