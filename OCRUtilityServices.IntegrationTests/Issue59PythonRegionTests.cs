using System.IO.Abstractions;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle.Python;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Rectangle = CoordinateUtilityServices.Rectangle;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonRegionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegionRecognition_CropsInputAndRestoresSourceCoordinates(bool useOptions)
    {
        using var source = new Image<Rgb24>(80, 60);
        source[20, 10] = new Rgb24(31, 73, 127);
        using var png = new MemoryStream();
        source.SaveAsPng(png);
        var runner = new CropRunner();
        var service = new PythonOcrUtilityService(new FileSystem(), runner);
        var region = Rectangle.FromXYWH(20, 10, 30, 25);

        var result = useOptions
            ? await service.RecognizeAsync(png.ToArray(),
                new OcrRecognitionOptions { Region = region, LanguageTag = "zh-TW" })
            : await service.RecognizeRegionAsync(png.ToArray(), region, "zh-TW");

        var line = Assert.Single(result.Lines);
        Assert.Equal("特惠禮包", line.Text);
        Assert.Equal(22, (int)line.Bounds.TopLeft.X);
        Assert.Equal(13, (int)line.Bounds.TopLeft.Y);
        Assert.Equal(37, (int)line.Bounds.BottomRight.X);
        Assert.Equal(22, (int)line.Bounds.BottomRight.Y);
        Assert.NotNull(runner.ImagePath);
        Assert.False(File.Exists(runner.ImagePath));
    }

    private sealed class CropRunner : IPythonOcrProcessRunner
    {
        public string? ImagePath { get; private set; }

        public Task<string> RunAsync(string imagePath, CancellationToken cancellationToken)
        {
            ImagePath = imagePath;
            using var crop = Image.Load<Rgb24>(imagePath);
            Assert.Equal(30, crop.Width);
            Assert.Equal(25, crop.Height);
            Assert.Equal(new Rgb24(31, 73, 127), crop[0, 0]);
            return Task.FromResult("""
                {"schemaVersion":"1.0.0","imageWidth":30,"imageHeight":25,
                 "coordinateSpace":"original_image_pixels",
                 "resolvedTextInstances":[{"text":"特惠禮包","confidence":0.99,
                   "bounds":{"left":2,"top":3,"right":17,"bottom":12},
                   "source":"adaptive_rescan","localizationStatus":"corroborated","corroborationCount":2}],
                 "unresolvedMergedBoxes":[]}
                """);
        }
    }
}
