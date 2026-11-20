using System.IO.Abstractions;
using FluentAssertions;
using OCRUtilityServices.Paddle.Python;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonBridgeEndToEndCandidateTests
{
    [Fact]
    public async Task RecognizeAsync_RealPythonFixture_ShouldPreserveOriginalCoordinates()
    {
        string? script = Environment.GetEnvironmentVariable("ISSUE59_BRIDGE_FIXTURE_SCRIPT");
        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException("ISSUE59_BRIDGE_FIXTURE_SCRIPT must point to the fixture Python script.");
        var fileSystem = new FileSystem();
        fileSystem.File.Exists(script).Should().BeTrue();
        string python = Environment.GetEnvironmentVariable("ISSUE59_PYTHON_EXECUTABLE") ?? "python";
        var runner = new PythonOcrProcessRunner(
            new PythonOcrProcessOptions(python, script, TimeSpan.FromSeconds(30)));
        var service = new PythonOcrUtilityService(fileSystem, runner);
        using var image = new Image<Rgb24>(2048,945);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);

        var result = await service.RecognizeAsync(buffer.ToArray(), CancellationToken.None);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Text.Should().Be("限時禮包");
        result.Lines[0].Bounds.TopLeft.X.Should().Be(1820);
        result.Lines[0].Bounds.TopLeft.Y.Should().Be(244);
        result.Lines[0].Bounds.BottomRight.X.Should().Be(1921);
        result.Lines[0].Bounds.BottomRight.Y.Should().Be(271);
        result.Text.Should().NotContain("外觀基金");
    }
}
