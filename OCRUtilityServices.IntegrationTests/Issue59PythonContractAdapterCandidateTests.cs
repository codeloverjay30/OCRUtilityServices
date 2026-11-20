using System.Text.Json;
using FluentAssertions;
using OCRUtilityServices.Paddle.Python;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonContractAdapterCandidateTests
{
    [Fact]
    public void Convert_ShouldExcludeUnresolvedAndUncorroboratedObservations()
    {
        string json = Envelope();
        var result = PythonOcrContractAdapter.Convert(json, 2048, 945);
        result.Lines.Should().ContainSingle();
        result.Lines[0].Text.Should().Be("限時禮包");
        result.Lines[0].Bounds.TopLeft.X.Should().Be(1820);
    }

    [Fact]
    public void Convert_ShouldRejectMismatchedScreenshotDimensions()
    {
        Action act = () => PythonOcrContractAdapter.Convert(Envelope(), 1024, 945);
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*image dimensions mismatch*");
    }

    [Fact]
    public void Convert_ShouldRejectMalformedJson()
    {
        Action act = () => PythonOcrContractAdapter.Convert("{", 2048, 945);
        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid OCR JSON payload*");
    }

    private static string Envelope() => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0.0",
        imageWidth = 2048,
        imageHeight = 945,
        coordinateSpace = "original_image_pixels",
        resolvedTextInstances = new object[]
        {
            new { text = "限時禮包", confidence = .99, bounds = new { left = 1820, top = 244, right = 1921, bottom = 271 }, source = "adaptive_rescan", localizationStatus = "corroborated", corroborationCount = 2 },
            new { text = "未確認", confidence = .99, bounds = new { left = 100, top = 100, right = 200, bottom = 130 }, source = "full_screen", localizationStatus = "original_detection", corroborationCount = 0 }
        },
        unresolvedMergedBoxes = new object[]
        {
            new { text = "外觀基金限時禮包", confidence = .9, bounds = new { left = 1163, top = 243, right = 1919, bottom = 270 }, source = "full_screen", localizationStatus = "ambiguous_merged_text", corroborationCount = 0 }
        }
    });
}
