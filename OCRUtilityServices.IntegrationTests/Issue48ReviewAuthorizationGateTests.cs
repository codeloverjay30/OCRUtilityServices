using System.IO.Abstractions;
using System.Text.Json;
using CoordinateUtilityServices;
using FluentAssertions;
using Moq;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48ReviewAuthorizationGateTests
{
    private readonly IFileSystem _fileSystem = new FileSystem();

    [Fact]
    public async Task Authorize_CurrentIssue48PendingReview_ShouldFailClosedWithoutCallingGeometry()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "TestData", "Issue48",
            "Reviews", "task-home.review.json");

        byte[] bytes = await _fileSystem.File.ReadAllBytesAsync(path);
        using JsonDocument document = JsonDocument.Parse(bytes);

        var geometry = new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict);
        var sut = new ReviewAwareGroundedClickAuthorizationService(geometry.Object);

        Action act = () => sut.Authorize(
            "任務", 0.999f, Rectangle.FromXYWH(556, 917, 51, 18),
            CreateTruth(), document.RootElement);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Review AnnotationDigestVerified must be true*");

        geometry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{}", "*Review Decision is missing or invalid*")]
    [InlineData("{\"Decision\":{}}", "*Review Integrity is missing or invalid*")]
    [InlineData("{\"Decision\":{},\"Integrity\":{},\"GeometryReview\":{}}",
        "*Review AnnotationDigestVerified must be true*")]
    public void Authorize_MissingReviewRequirements_ShouldFailClosed(
        string json, string expectedMessage)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var geometry = new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict);
        var sut = new ReviewAwareGroundedClickAuthorizationService(geometry.Object);

        // Use an actual valid ReviewedGroundTruth instance in the project-specific
        // tests below; these structural tests use the factory defined here.
        Action act = () => sut.Authorize(
            "任務", 0.999f, Rectangle.FromXYWH(556, 917, 51, 18),
            CreateTruth(), document.RootElement);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);
        geometry.VerifyNoOtherCalls();
    }

    [Fact]
    public void Authorize_AllReviewFlagsPresentButPending_ShouldFailClosed()
    {
        using JsonDocument document = JsonDocument.Parse(ApprovedReviewJson
            .Replace("\"Approved\"", "\"PendingGeometryReview\"", StringComparison.Ordinal));
        var geometry = new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict);
        var sut = new ReviewAwareGroundedClickAuthorizationService(geometry.Object);

        Action act = () => sut.Authorize(
            "任務", 0.999f, Rectangle.FromXYWH(556, 917, 51, 18),
            CreateTruth(), document.RootElement);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Review decision is not Approved*");
        geometry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("GroundTruthApproved")]
    [InlineData("ClickAuthorized")]
    public void Authorize_MissingDecisionApproval_ShouldFailClosed(string flag)
    {
        string json = ApprovedReviewJson.Replace(
            $"\"{flag}\":true", $"\"{flag}\":false", StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        var geometry = new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict);
        var sut = new ReviewAwareGroundedClickAuthorizationService(geometry.Object);

        Action act = () => sut.Authorize(
            "任務", 0.999f, Rectangle.FromXYWH(556, 917, 51, 18),
            CreateTruth(), document.RootElement);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Review {flag} must be true*");
        geometry.VerifyNoOtherCalls();
    }

    [Fact]
    public void Authorize_ApprovedReview_ShouldDelegateToGeometryWithoutClick()
    {
        using JsonDocument document = JsonDocument.Parse(ApprovedReviewJson);
        var truth = CreateTruth();
        Rectangle detected = Rectangle.FromXYWH(556, 917, 51, 18);
        var geometry = new Mock<IGroundedClickAuthorizationService>(MockBehavior.Strict);
        geometry.Setup(x => x.Authorize("任務", 0.999f, detected, truth))
            .Returns((581.5, 926d));
        var sut = new ReviewAwareGroundedClickAuthorizationService(geometry.Object);

        (double X, double Y) point = sut.Authorize(
            "任務", 0.999f, detected, truth, document.RootElement);

        point.X.Should().Be(581.5);
        point.Y.Should().Be(926);
        geometry.Verify(x => x.Authorize("任務", 0.999f, detected, truth), Times.Once);
        geometry.VerifyNoOtherCalls();
    }

    private const string ApprovedReviewJson = """
        {
          "Decision":{"Status":"Approved","GroundTruthApproved":true,"ClickAuthorized":true},
          "Integrity":{"AnnotationDigestVerified":true,"ScreenshotDigestVerified":true,"ScreenshotDimensionsVerified":true},
          "GeometryReview":{"TextBoundsConfirmed":true,"ButtonBoundsConfirmed":true,"SafeClickAreaConfirmed":true}
        }
        """;

    private static ReviewedGroundTruth CreateTruth() => new(
        "task-home-original.png",
        "任務",
        Rectangle.FromXYWH(556, 917, 51, 18),
        Rectangle.FromXYWH(515, 815, 135, 130),
        2340,
        1080);
}
