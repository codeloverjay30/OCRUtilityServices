using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class GroundTruthLoaderTests : IDisposable
{
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly string _directory;
    private readonly IGroundTruthLoader _sut;

    public GroundTruthLoaderTests()
    {
        _directory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(), "ground-truth-tests-" + Guid.NewGuid().ToString("N"));
        _fileSystem.Directory.CreateDirectory(_directory);
        _sut = new GroundTruthLoader(_fileSystem);
    }

    [Fact]
    public async Task LoadAsync_ReviewedAndDigestPinned_ShouldReturnVerifiedGeometry()
    {
        Fixture fixture = await CreateFixtureAsync();

        ReviewedGroundTruth truth = await LoadAsync(fixture);

        truth.Fixture.Should().Be("screen.png");
        truth.TargetText.Should().Be("登入遊戲");
        truth.ImageWidth.Should().Be(240);
        truth.ImageHeight.Should().Be(120);
        truth.TextBounds.TopLeft.X.Should().Be(100);
        truth.TextBounds.TopLeft.Y.Should().Be(50);
        truth.ButtonBounds.Width.Should().Be(100);
        truth.ButtonBounds.Height.Should().Be(50);
    }

    [Fact]
    public async Task LoadAsync_ReviewedGeometry_ShouldAuthorizeClick()
    {
        Fixture fixture = await CreateFixtureAsync();
        ReviewedGroundTruth truth = await LoadAsync(fixture);
        IGroundedClickAuthorizationService authorization =
            new GroundedClickAuthorizationService(new GroundedClickValidator());

        (double X, double Y) point = authorization.Authorize(
            "登入遊戲", 0.999f,
            CoordinateUtilityServices.Rectangle.FromXYWH(100, 50, 60, 20), truth);

        point.X.Should().Be(130);
        point.Y.Should().Be(60);
    }

    [Fact]
    public async Task LoadAsync_ModifiedAnnotation_ShouldRejectDigestMismatch()
    {
        Fixture fixture = await CreateFixtureAsync();
        await _fileSystem.File.AppendAllTextAsync(fixture.AnnotationPath, " ");

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*Annotation SHA-256 does not match*");
    }

    [Fact]
    public async Task LoadAsync_ModifiedScreenshot_ShouldRejectDigestMismatch()
    {
        Fixture fixture = await CreateFixtureAsync();
        await _fileSystem.File.AppendAllTextAsync(fixture.ScreenshotPath, "tampered");

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*Screenshot SHA-256 does not match*");
    }

    [Theory]
    [InlineData("PendingReview", "Reviewer", "screen.png", "*has not been reviewed*")]
    [InlineData("Reviewed", "", "screen.png", "*reviewer is missing*")]
    [InlineData("Reviewed", "Reviewer", "another.png", "*fixture does not match*")]
    public async Task LoadAsync_InvalidReviewMetadata_ShouldFailClosed(
        string status, string reviewer, string fixtureName, string expectedMessage)
    {
        Fixture fixture = await CreateFixtureAsync(status: status, reviewer: reviewer,
            fixtureName: fixtureName);

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage(expectedMessage);
    }

    [Theory]
    [InlineData(-1, 50, 60, 20)]
    [InlineData(100, 50, 0, 20)]
    [InlineData(200, 50, 60, 20)]
    [InlineData(100, 115, 60, 20)]
    public async Task LoadAsync_InvalidTextRectangle_ShouldFailClosed(
        double x, double y, double width, double height)
    {
        Fixture fixture = await CreateFixtureAsync(textBounds: new Bounds(x, y, width, height));

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*rectangle is invalid*");
    }

    [Fact]
    public async Task LoadAsync_MissingButtonBounds_ShouldFailClosed()
    {
        Fixture fixture = await CreateFixtureAsync(includeButton: false);

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*bounds are missing*");
    }

    [Fact]
    public async Task LoadAsync_ImageDimensionsMismatch_ShouldFailClosed()
    {
        Fixture fixture = await CreateFixtureAsync(imageWidth: 241);

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*image dimensions do not match*");
    }

    [Fact]
    public async Task LoadAsync_CorruptScreenshotWithMatchingDigest_ShouldFailClosed()
    {
        Fixture fixture = await CreateFixtureAsync();
        byte[] corrupted = Encoding.UTF8.GetBytes("not a valid PNG");
        await _fileSystem.File.WriteAllBytesAsync(fixture.ScreenshotPath, corrupted);
        fixture = fixture with { ScreenshotSha256 = Hash(corrupted) };

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*screenshot cannot be decoded*");
    }

    [Fact]
    public async Task LoadAsync_InvalidJsonWithMatchingDigest_ShouldFailClosed()
    {
        Fixture fixture = await CreateFixtureAsync();
        byte[] invalid = Encoding.UTF8.GetBytes("{ invalid json");
        await _fileSystem.File.WriteAllBytesAsync(fixture.AnnotationPath, invalid);
        fixture = fixture with { AnnotationSha256 = Hash(invalid) };

        Func<Task> act = async () => await LoadAsync(fixture);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*JSON is invalid*");
    }

    [Fact]
    public async Task LoadAsync_InvalidExpectedDigest_ShouldRejectBeforeReading()
    {
        Fixture fixture = await CreateFixtureAsync();

        Func<Task> act = async () => await _sut.LoadAsync(
            fixture.AnnotationPath, fixture.ScreenshotPath,
            "not-a-sha256", fixture.ScreenshotSha256);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*64 hexadecimal characters*");
    }

    [Fact]
    public async Task LoadAsync_CancelledToken_ShouldNotLoad()
    {
        Fixture fixture = await CreateFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = async () => await _sut.LoadAsync(
            fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.AnnotationSha256, fixture.ScreenshotSha256, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        if (_fileSystem.Directory.Exists(_directory))
        {
            _fileSystem.Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<ReviewedGroundTruth> LoadAsync(Fixture fixture)
        => await _sut.LoadAsync(
            fixture.AnnotationPath, fixture.ScreenshotPath,
            fixture.AnnotationSha256, fixture.ScreenshotSha256);

    private async Task<Fixture> CreateFixtureAsync(
        string status = "Reviewed",
        string reviewer = "Reviewer",
        string fixtureName = "screen.png",
        Bounds? textBounds = null,
        bool includeButton = true,
        int imageWidth = 240)
    {
        string screenshotPath = _fileSystem.Path.Combine(_directory, "screen.png");
        string annotationPath = _fileSystem.Path.Combine(_directory, "screen.json");

        using var image = new Image<Rgba32>(240, 120);
        using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        byte[] screenshotBytes = stream.ToArray();
        await _fileSystem.File.WriteAllBytesAsync(screenshotPath, screenshotBytes);

        var document = new
        {
            Fixture = fixtureName,
            TargetText = "登入遊戲",
            Status = status,
            Reviewer = reviewer,
            ImageWidth = imageWidth,
            ImageHeight = 120,
            TextBounds = textBounds ?? new Bounds(100, 50, 60, 20),
            ButtonBounds = includeButton ? new Bounds(80, 35, 100, 50) : null
        };
        byte[] annotationBytes = JsonSerializer.SerializeToUtf8Bytes(document);
        await _fileSystem.File.WriteAllBytesAsync(annotationPath, annotationBytes);

        return new Fixture(annotationPath, screenshotPath,
            Hash(annotationBytes), Hash(screenshotBytes));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Bounds(double X, double Y, double Width, double Height);
    private sealed record Fixture(
        string AnnotationPath, string ScreenshotPath,
        string AnnotationSha256, string ScreenshotSha256);
}
