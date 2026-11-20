using System.IO.Abstractions;
using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Paddle.Python;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonServiceSafetyTests
{
    private readonly IFileSystem _fileSystem = new FileSystem();

    [Fact]
    public async Task RecognizeAsync_ValidContract_ShouldDeleteTemporaryScreenshot()
    {
        string? capturedPath = null;
        var runner = new StubRunner((path, _) =>
        {
            capturedPath = path;
            _fileSystem.File.Exists(path).Should().BeTrue();
            return Task.FromResult(ValidEnvelope());
        });
        var service = new PythonOcrUtilityService(_fileSystem, runner);

        var result = await service.RecognizeAsync(CreateImage(), CancellationToken.None);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Text.Should().Be("限時禮包");
        capturedPath.Should().NotBeNullOrWhiteSpace();
        _fileSystem.File.Exists(capturedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task RecognizeAsync_ProcessFailure_ShouldDeleteTemporaryScreenshotAndPropagate()
    {
        string? capturedPath = null;
        var runner = new StubRunner((path, _) =>
        {
            capturedPath = path;
            _fileSystem.File.Exists(path).Should().BeTrue();
            throw new InvalidOperationException("Python OCR exited with code 23");
        });
        var service = new PythonOcrUtilityService(_fileSystem, runner);

        Func<Task> act = () => service.RecognizeAsync(CreateImage(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*code 23*");
        capturedPath.Should().NotBeNullOrWhiteSpace();
        _fileSystem.File.Exists(capturedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task RecognizeAsync_InvalidContract_ShouldDeleteTemporaryScreenshot()
    {
        string? capturedPath = null;
        var runner = new StubRunner((path, _) =>
        {
            capturedPath = path;
            return Task.FromResult("{");
        });
        var service = new PythonOcrUtilityService(_fileSystem, runner);

        Func<Task> act = () => service.RecognizeAsync(CreateImage(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*Invalid OCR JSON payload*");
        capturedPath.Should().NotBeNullOrWhiteSpace();
        _fileSystem.File.Exists(capturedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task RecognizeAsync_CancelledDuringProcess_ShouldDeleteTemporaryScreenshot()
    {
        string? capturedPath = null;
        using var cancellation = new CancellationTokenSource();
        var runner = new StubRunner((path, token) =>
        {
            capturedPath = path;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ValidEnvelope());
        });
        var service = new PythonOcrUtilityService(_fileSystem, runner);

        Func<Task> act = () => service.RecognizeAsync(CreateImage(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        capturedPath.Should().NotBeNullOrWhiteSpace();
        _fileSystem.File.Exists(capturedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task RecognizeAsync_PreCancelled_ShouldNotInvokeProcess()
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new StubRunner((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(ValidEnvelope());
        });
        var service = new PythonOcrUtilityService(_fileSystem, runner);

        Func<Task> act = () => service.RecognizeAsync(CreateImage(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(0);
    }

    private static byte[] CreateImage()
    {
        using var image = new Image<Rgb24>(2048, 945);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static string ValidEnvelope() => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0.0",
        imageWidth = 2048,
        imageHeight = 945,
        coordinateSpace = "original_image_pixels",
        resolvedTextInstances = new object[]
        {
            new
            {
                text = "限時禮包", confidence = 0.999,
                bounds = new { left = 1820, top = 244, right = 1921, bottom = 271 },
                source = "adaptive_rescan", localizationStatus = "corroborated",
                corroborationCount = 2
            }
        },
        unresolvedMergedBoxes = Array.Empty<object>()
    });

    private sealed class StubRunner : IPythonOcrProcessRunner
    {
        private readonly Func<string, CancellationToken, Task<string>> _run;

        public StubRunner(Func<string, CancellationToken, Task<string>> run) => _run = run;

        public Task<string> RunAsync(string imagePath, CancellationToken cancellationToken) =>
            _run(imagePath, cancellationToken);
    }
}
