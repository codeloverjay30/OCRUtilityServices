
using System.IO.Abstractions;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime.Tensors;
using OCRUtilityServices.Paddle.V6;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;
using SixLabors.ImageSharp.Processing;
using OCRUtilityServices.Services;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle;

namespace OCRUtilityServices.IntegrationTests;

/// <summary>
/// Validates the PaddleOCR v6 ONNX recognition pipeline against real
/// Traditional Chinese OCR fixtures and exported model assets.
/// </summary>
public sealed class PaddleOcrV6OnnxIntegrationTests
{
    private const string ModelDirectoryVariable = "PADDLEOCR_V6_MODEL_DIR";
    private const string CropsDirectoryVariable = "PADDLEOCR_V6_CROPS_DIR";
    private const int ExpectedClasses = 18710;
    private const float MinimumConfidence = 0.99f;

    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes the integration test suite.
    /// </summary>
    public PaddleOcrV6OnnxIntegrationTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Theory]
    [InlineData("ocr-diagnostic-000003.png")]
    [InlineData("ocr-diagnostic-000004.png")]
    [InlineData("ocr-diagnostic-000005.png")]
    [InlineData("ocr-diagnostic-000006.png")]
    [InlineData("ocr-diagnostic-000007.png")]
    [InlineData("ocr-diagnostic-000008.png")]
    [InlineData("ocr-diagnostic-000009.png")]
    [InlineData("ocr-diagnostic-000010.png")]
    [InlineData("ocr-diagnostic-000011.png")]
    [InlineData("ocr-diagnostic-000012.png")]
    [Trait("Category", "PaddleOcrV6OnnxIntegration")]
    public void Recognize_RealCrop_ShouldRecognizeTraditionalChinese(
        string fixtureName)
    {
        string modelDirectory = RequireDirectory(ModelDirectoryVariable);
        string cropsDirectory = RequireDirectory(CropsDirectoryVariable);

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(cropsDirectory, fixtureName));

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        const int left = 1098;
        const int top = 736;
        const int width = 139;
        const int height = 37;


        ((long)left + width).Should().BeLessThanOrEqualTo(screenshot.Width);
        ((long)top + height).Should().BeLessThanOrEqualTo(screenshot.Height);

        using Image<Rgb24> image = screenshot.Clone(
            context => context.Crop(new Rectangle(left, top, width, height)));

        _output.WriteLine(
            $"Fixture={fixtureName}, Screenshot={screenshot.Width}x{screenshot.Height}, " +
            $"Crop={image.Width}x{image.Height}");

        _output.WriteLine(
            $"Fixture={fixtureName}, ImageSize={image.Width}x{image.Height}");
    
        var preprocessor = new PaddleOcrV6ImagePreprocessor();
        using var inferenceEngine =
            new PaddleOcrV6OnnxInferenceEngine(modelPath);

        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);

        using var recognizer = new PaddleOcrV6Recognizer(
            preprocessor,
            inferenceEngine,
            decoder);

        (string text, float score) = recognizer.Recognize(image);

        _output.WriteLine(
            $"Fixture={fixtureName}, Text=[{text}], Score={score:G9}");

        text.Should().Be(
            "登入遊戲",
            $"the real ONNX model must recognize fixture {fixtureName}");

        float.IsFinite(score).Should().BeTrue();

        score.Should().BeGreaterThan(
            MinimumConfidence,
            $"fixture {fixtureName} must have high recognition confidence");

        score.Should().BeLessThanOrEqualTo(1f);
    }

    [Fact]
    [Trait("Category", "PaddleOcrV6OnnxIntegration")]
    public void Run_RealOnnxModel_ShouldReturnExpectedCtcTensor()
    {
        string modelDirectory = RequireDirectory(ModelDirectoryVariable);
        string cropsDirectory = RequireDirectory(CropsDirectoryVariable);

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(
                cropsDirectory,
                "ocr-diagnostic-000007.png"));

        using Image<Rgb24> image = LoadImage(fixturePath);

        var preprocessor = new PaddleOcrV6ImagePreprocessor();

        DenseTensor<float> input = preprocessor.Preprocess(image);

        input.Dimensions.ToArray().Should().Equal(
            1, 3, 48, 320);

        using var inferenceEngine =
            new PaddleOcrV6OnnxInferenceEngine(modelPath);

        (float[] values, int steps, int classes) =
            inferenceEngine.Run(input);

        _output.WriteLine(
            $"Steps={steps}, Classes={classes}, Values={values.Length}");

        steps.Should().BeGreaterThan(0);
        classes.Should().Be(ExpectedClasses);

        values.Length.Should().Be(
            checked(steps * classes));

        values.Should().OnlyContain(value => float.IsFinite(value));
    }

    [Fact]
    [Trait("Category", "PaddleOcrV6Diagnostic")]
    public void ComparePreprocessing_Fixture000007_ShouldReportFourStrategies()
    {
        string modelDirectory = RequireDirectory(ModelDirectoryVariable);
        string fixtureDirectory = RequireDirectory(CropsDirectoryVariable);

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(
                fixtureDirectory, "ocr-diagnostic-000007.png"));

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        const int left = 1104;
        const int top = 742;
        const int width = 127;
        const int height = 25;

        ((long)left + width).Should().BeLessThanOrEqualTo(screenshot.Width);
        ((long)top + height).Should().BeLessThanOrEqualTo(screenshot.Height);

        using Image<Rgb24> crop = screenshot.Clone(
            context => context.Crop(
                new Rectangle(left, top, width, height)));

        using var engine = new PaddleOcrV6OnnxInferenceEngine(modelPath);
        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);

        foreach (bool preserveAspectRatio in new[] { true, false })
        {
            foreach (bool useBgr in new[] { true, false })
            {
                DenseTensor<float> tensor = BuildDiagnosticTensor(
                    crop,
                    preserveAspectRatio,
                    useBgr);

                (float[] values, int steps, int classes) = engine.Run(tensor);

                (string text, float score) =
                    decoder.Decode(values, steps, classes);

                _output.WriteLine(
                    $"AspectRatio={preserveAspectRatio}, " +
                    $"BGR={useBgr}, " +
                    $"Text=[{text}], " +
                    $"Score={score:G9}");
            }
        }
    }

    [Fact]
    [Trait("Category", "PaddleOcrV6Diagnostic")]
    public void DiagnoseCtc_Fixture000007_ShouldReportTargetCharacterCandidates()
    {
        string modelDirectory = RequireDirectory(ModelDirectoryVariable);
        string cropsDirectory = RequireDirectory(CropsDirectoryVariable);

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(
                cropsDirectory, "ocr-diagnostic-000007.png"));

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        const int left = 1104;
        const int top = 742;
        const int width = 127;
        const int height = 25;

        left.Should().BeGreaterThanOrEqualTo(0);
        top.Should().BeGreaterThanOrEqualTo(0);
        ((long)left + width).Should().BeLessThanOrEqualTo(screenshot.Width);
        ((long)top + height).Should().BeLessThanOrEqualTo(screenshot.Height);

        using Image<Rgb24> crop = screenshot.Clone(
            context => context.Crop(
                new Rectangle(left, top, width, height)));

        var preprocessor = new PaddleOcrV6ImagePreprocessor();
        DenseTensor<float> input = preprocessor.Preprocess(crop);

        using var engine = new PaddleOcrV6OnnxInferenceEngine(modelPath);
        (float[] values, int steps, int classes) = engine.Run(input);

        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);
        (string text, float confidence) = decoder.Decode(
            values, steps, classes);

        using Stream dictionaryStream = _fileSystem.File.OpenRead(dictionaryPath);
        string[] dictionary =
            System.Text.Json.JsonSerializer.Deserialize<string[]>(dictionaryStream)
            ?? throw new InvalidDataException("OCR dictionary is null.");

        dictionary.Length.Should().Be(18708);

        int targetIndex = Array.IndexOf(dictionary, "戲");
        targetIndex.Should().BeGreaterThanOrEqualTo(
            0, "the model dictionary must contain the target character");

        int targetClass = targetIndex + 1;

        _output.WriteLine(
            $"Decoded=[{text}], Confidence={confidence:G9}");
        _output.WriteLine(
            $"Target=戲, ClassIndex={targetClass}, Steps={steps}");

        float highestTargetScore = float.NegativeInfinity;
        int highestTargetStep = -1;

        for (int step = 0; step < steps; step++)
        {
            ReadOnlySpan<float> scores =
                values.AsSpan(checked(step * classes), classes);

            float targetScore = scores[targetClass];

            if (targetScore > highestTargetScore)
            {
                highestTargetScore = targetScore;
                highestTargetStep = step;
            }

            const int topK = 5;

            var bestIndices = new int[topK];
            var bestScores = new float[topK];
            int bestCount = 0;

            for (int classIndex = 0; classIndex < classes; classIndex++)
            {
                float candidateScore = scores[classIndex];

                float.IsFinite(candidateScore).Should().BeTrue(
                    $"CTC score at step {step}, class {classIndex} must be finite");

                int insertAt = 0;

                while (insertAt < bestCount &&
                       (bestScores[insertAt] > candidateScore ||
                        (bestScores[insertAt] == candidateScore &&
                         bestIndices[insertAt] < classIndex)))
                {
                    insertAt++;
                }

                if (insertAt >= topK)
                {
                    continue;
                }

                if (bestCount < topK)
                {
                    bestCount++;
                }

                for (int position = bestCount - 1; position > insertAt; position--)
                {
                    bestIndices[position] = bestIndices[position - 1];
                    bestScores[position] = bestScores[position - 1];
                }

                bestIndices[insertAt] = classIndex;
                bestScores[insertAt] = candidateScore;
            }

            var topCandidates = new string[bestCount];

            for (int index = 0; index < bestCount; index++)
            {
                int classIndex = bestIndices[index];

                string label = classIndex == 0
                    ? "<BLANK>"
                    : classIndex == classes - 1
                        ? "<SPACE>"
                        : dictionary[classIndex - 1];

                topCandidates[index] =
                    $"{classIndex}:{label}:{bestScores[index]:G9}";
            }


            _output.WriteLine(
                $"T={step:D3}, " +
                $"TargetScore={targetScore:G9}, " +
                $"Top5=[{string.Join(" | ", topCandidates)}]");
        }

        _output.WriteLine(
            $"HighestTargetScore={highestTargetScore:G9}, " +
            $"AtStep={highestTargetStep}");
    }


    [Fact]
    [Trait("Category", "PaddleOcrV6RoiDiagnostic")]
    public void DiagnoseRoiSensitivity_Fixture000007_ShouldReportRecognitionAndCtcScores()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string expectedText = "登入遊戲";

        const int baseLeft = 1104;
        const int baseTop = 742;
        const int baseWidth = 127;
        const int baseHeight = 25;

        string modelDirectory = RequireDirectory(ModelDirectoryVariable);
        string fixtureDirectory = RequireDirectory(CropsDirectoryVariable);

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(fixtureDirectory, fixtureName));

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        using Stream dictionaryStream = _fileSystem.File.OpenRead(dictionaryPath);

        string[] dictionary =
            System.Text.Json.JsonSerializer.Deserialize<string[]>(dictionaryStream)
            ?? throw new InvalidDataException("OCR dictionary is null.");

        dictionary.Length.Should().Be(ExpectedClasses - 2);

        int targetClass = Array.IndexOf(dictionary, "戲") + 1;
        int wrongClass = Array.IndexOf(dictionary, "度") + 1;
        int alternateClass = Array.IndexOf(dictionary, "虞") + 1;

        targetClass.Should().BeGreaterThan(0);
        wrongClass.Should().BeGreaterThan(0);
        alternateClass.Should().BeGreaterThan(0);

        using var engine = new PaddleOcrV6OnnxInferenceEngine(modelPath);
        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);
        var preprocessor = new PaddleOcrV6ImagePreprocessor();

        int experimentCount = 0;
        int exactMatchCount = 0;

        float bestTargetProbability = float.NegativeInfinity;
        string bestConfiguration = string.Empty;

        foreach (int margin in new[] { 0, 2, 4, 6 })
        {
            int left = baseLeft - margin;
            int top = baseTop - margin;
            int width = checked(baseWidth + 2 * margin);
            int height = checked(baseHeight + 2 * margin);

            left.Should().BeGreaterThanOrEqualTo(0);
            top.Should().BeGreaterThanOrEqualTo(0);
            ((long)left + width).Should().BeLessThanOrEqualTo(screenshot.Width);
            ((long)top + height).Should().BeLessThanOrEqualTo(screenshot.Height);

            using Image<Rgb24> crop = screenshot.Clone(
                context => context.Crop(
                    new Rectangle(left, top, width, height)));

            foreach (int scale in new[] { 1, 2, 3 })
            {
                using Image<Rgb24> candidate = scale == 1
                    ? crop.Clone()
                    : crop.Clone(context => context.Resize(
                        new ResizeOptions
                        {
                            Size = new Size(
                                checked(width * scale),
                                checked(height * scale)),
                            Mode = ResizeMode.Stretch,
                            Sampler = KnownResamplers.Triangle
                        }));

                DenseTensor<float> tensor =
                    preprocessor.Preprocess(candidate);

                (float[] values, int steps, int classes) =
                    engine.Run(tensor);

                classes.Should().Be(ExpectedClasses);
                steps.Should().BeGreaterThan(0);
                values.Length.Should().Be(checked(steps * classes));

                (string text, float confidence) =
                    decoder.Decode(values, steps, classes);

                float targetMaximum = float.NegativeInfinity;
                int targetStep = -1;

                for (int step = 0; step < steps; step++)
                {
                    ReadOnlySpan<float> row =
                        values.AsSpan(checked(step * classes), classes);

                    float probability = row[targetClass];

                    if (probability > targetMaximum)
                    {
                        targetMaximum = probability;
                        targetStep = step;
                    }
                }

                targetStep.Should().BeGreaterThanOrEqualTo(0);

                ReadOnlySpan<float> targetRow =
                    values.AsSpan(checked(targetStep * classes), classes);

                float degreeProbability = targetRow[wrongClass];
                float alternateProbability = targetRow[alternateClass];

                bool exactMatch =
                    string.Equals(text, expectedText, StringComparison.Ordinal);

                experimentCount++;

                if (exactMatch)
                {
                    exactMatchCount++;
                }

                if (targetMaximum > bestTargetProbability)
                {
                    bestTargetProbability = targetMaximum;
                    bestConfiguration =
                        $"Margin={margin}, Scale={scale}, " +
                        $"Crop={left},{top},{width},{height}, " +
                        $"Text=[{text}], Confidence={confidence:G9}";
                }

                _output.WriteLine(
                    $"Margin={margin}, " +
                    $"Scale={scale}, " +
                    $"Crop={left},{top},{width},{height}, " +
                    $"Text=[{text}], " +
                    $"Confidence={confidence:G9}, " +
                    $"ExactMatch={exactMatch}, " +
                    $"TargetStep={targetStep}, " +
                    $"P(戲)={targetMaximum:G9}, " +
                    $"P(度)={degreeProbability:G9}, " +
                    $"P(虞)={alternateProbability:G9}");
            }
        }

        experimentCount.Should().Be(12);
        float.IsFinite(bestTargetProbability).Should().BeTrue();

        _output.WriteLine(
            $"SUMMARY: Experiments={experimentCount}, " +
            $"ExactMatches={exactMatchCount}");

        _output.WriteLine(
            $"SUMMARY: HighestP(戲)={bestTargetProbability:G9}");

        _output.WriteLine(
            $"SUMMARY: BestTargetConfiguration={bestConfiguration}");
    }


    [Fact]
    [Trait("Category", "PaddleOcrV3DetectionV6Recognition")]
    public async Task Recognize_DetectedLoginBoundsWithPadding_ShouldReadTraditionalChinese()
    {
        const string fixtureName = "ocr-diagnostic-000007.png";
        const string expectedText = "登入遊戲";
        const int padding = 6;
        const float minimumConfidence = 0.99f;

        string modelDirectory = RequireDirectory("PADDLEOCR_V6_MODEL_DIR");
        string fixtureDirectory = RequireDirectory("PADDLEOCR_FIXTURE_DIR");

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(fixtureDirectory, fixtureName));

        string detectionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DETECTION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DETECTION_MODEL is not configured."));

        string recognitionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_RECOGNITION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_RECOGNITION_MODEL is not configured."));

        string legacyDictionaryPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DICTIONARY")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DICTIONARY is not configured."));

        byte[] screenshotBytes = await _fileSystem.File.ReadAllBytesAsync(fixturePath);

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        var options = new PaddleOcrOptions
        {
            DetectionModelPath = detectionPath,
            RecognitionModelPath = recognitionPath,
            DictionaryPath = legacyDictionaryPath
        };

        using var backend = new PaddleOcrOnnxBackend(options);
        IOCRUtilityService detector = new PaddleOcrUtilityService(backend);

        OcrResult detectionResult = await detector.RecognizeAsync(
            screenshotBytes,
            "zh-TW");

        // Only use the geometry from the legacy pipeline.
        // Its recognized text is deliberately ignored.
        var candidates = detectionResult.Lines
            .Where(line =>
                line.Bounds.Width > 0 &&
                line.Bounds.Height > 0 &&
                line.Bounds.TopLeft.X >= 0 &&
                line.Bounds.TopLeft.Y >= 0 &&
                line.Bounds.BottomRight.X <= screenshot.Width &&
                line.Bounds.BottomRight.Y <= screenshot.Height)
            .Where(line =>
            {
                double centerX = line.Bounds.Center.X;
                double centerY = line.Bounds.Center.Y;

                // Diagnostic search region, not a click target.
                return centerX >= 900 &&
                       centerX <= 1400 &&
                       centerY >= 700 &&
                       centerY <= 820;
            })
            .ToArray();

        candidates.Should().ContainSingle(
            "the diagnostic screenshot should have exactly one detected " +
            "text region near the known login label");

        OcrTextLine candidate = candidates.Single();

        int left = Math.Max(
            0,
            (int)Math.Floor(candidate.Bounds.TopLeft.X) - padding);

        int top = Math.Max(
            0,
            (int)Math.Floor(candidate.Bounds.TopLeft.Y) - padding);

        int right = Math.Min(
            screenshot.Width,
            (int)Math.Ceiling(candidate.Bounds.BottomRight.X) + padding);

        int bottom = Math.Min(
            screenshot.Height,
            (int)Math.Ceiling(candidate.Bounds.BottomRight.Y) + padding);

        int width = right - left;
        int height = bottom - top;

        width.Should().BeGreaterThan(0);
        height.Should().BeGreaterThan(0);

        using Image<Rgb24> crop = screenshot.Clone(
            context => context.Crop(
                new SixLabors.ImageSharp.Rectangle(left, top, width, height)));

        var preprocessor = new PaddleOcrV6ImagePreprocessor();

        using var inferenceEngine =
            new PaddleOcrV6OnnxInferenceEngine(modelPath);

        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);

        using var recognizer = new PaddleOcrV6Recognizer(
            preprocessor,
            inferenceEngine,
            decoder);

        (string text, float score) = recognizer.Recognize(crop);

        _output.WriteLine(
            $"Fixture={fixtureName}, " +
            $"DetectedText=[{candidate.Text}], " +
            $"DetectedBounds={candidate.Bounds}, " +
            $"ExpandedCrop=({left},{top},{width},{height}), " +
            $"V6Text=[{text}], V6Score={score:G9}");

        text.Should().Be(
            expectedText,
            "V6 recognition should recover the Traditional Chinese " +
            "login label from a V3-detected text region");

        float.IsFinite(score).Should().BeTrue();
        score.Should().BeGreaterThan(minimumConfidence);
        score.Should().BeLessThanOrEqualTo(1f);

        double centerX = candidate.Bounds.Center.X;
        double centerY = candidate.Bounds.Center.Y;

        centerX.Should().BeInRange(0, screenshot.Width - 1);
        centerY.Should().BeInRange(0, screenshot.Height - 1);

        _output.WriteLine(
            $"CandidateTextCenter=({centerX:G9},{centerY:G9}); " +
            "ClickSafety=NOT_VERIFIED");
    }


    [Theory]
    [InlineData("ocr-diagnostic-000003.png")]
    [InlineData("ocr-diagnostic-000004.png")]
    [InlineData("ocr-diagnostic-000005.png")]
    [InlineData("ocr-diagnostic-000006.png")]
    [InlineData("ocr-diagnostic-000007.png")]
    [InlineData("ocr-diagnostic-000008.png")]
    [InlineData("ocr-diagnostic-000009.png")]
    [InlineData("ocr-diagnostic-000010.png")]
    [InlineData("ocr-diagnostic-000011.png")]
    [InlineData("ocr-diagnostic-000012.png")]
    [Trait("Category", "PaddleOcrV3DetectionV6FullScreen")]
    public async Task Recognize_FullScreenshot_ShouldFindUniqueLoginTextUsingV6(
        string fixtureName)
    {
        const string targetText = "登入遊戲";
        const int padding = 8;
        const float minimumConfidence = 0.99f;

        string modelDirectory = RequireDirectory("PADDLEOCR_V6_MODEL_DIR");
        string fixtureDirectory = RequireDirectory("PADDLEOCR_FIXTURE_DIR");

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(fixtureDirectory, fixtureName));

        string detectionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DETECTION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DETECTION_MODEL is not configured."));

        string recognitionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_RECOGNITION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_RECOGNITION_MODEL is not configured."));

        string legacyDictionaryPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DICTIONARY")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DICTIONARY is not configured."));

        byte[] screenshotBytes =
            await _fileSystem.File.ReadAllBytesAsync(fixturePath);

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        var options = new PaddleOcrOptions
        {
            DetectionModelPath = detectionPath,
            RecognitionModelPath = recognitionPath,
            DictionaryPath = legacyDictionaryPath
        };

        using var backend = new PaddleOcrOnnxBackend(options);

        IOCRUtilityService detector =
            new PaddleOcrUtilityService(backend);

        OcrResult detectionResult = await detector.RecognizeAsync(
            screenshotBytes,
            "zh-TW");

        detectionResult.Lines.Should().NotBeEmpty(
            "the detector must return candidate text regions");

        var preprocessor = new PaddleOcrV6ImagePreprocessor();

        using var inferenceEngine =
            new PaddleOcrV6OnnxInferenceEngine(modelPath);

        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);

        using var recognizer = new PaddleOcrV6Recognizer(
            preprocessor,
            inferenceEngine,
            decoder);

        var matches = new List<(
            OcrTextLine Line,
            string Text,
            float Score)>();

        int candidateCount = 0;

        foreach (OcrTextLine line in detectionResult.Lines)
        {
            double x1 = line.Bounds.TopLeft.X;
            double y1 = line.Bounds.TopLeft.Y;
            double x2 = line.Bounds.BottomRight.X;
            double y2 = line.Bounds.BottomRight.Y;

            bool validGeometry =
                double.IsFinite(x1) &&
                double.IsFinite(y1) &&
                double.IsFinite(x2) &&
                double.IsFinite(y2) &&
                x1 >= 0 &&
                y1 >= 0 &&
                x2 > x1 &&
                y2 > y1 &&
                x2 <= screenshot.Width &&
                y2 <= screenshot.Height;

            validGeometry.Should().BeTrue(
                $"detected line geometry must be valid: {line.Bounds}");

            int left = Math.Max(
                0,
                checked((int)Math.Floor(x1)) - padding);

            int top = Math.Max(
                0,
                checked((int)Math.Floor(y1)) - padding);

            int right = Math.Min(
                screenshot.Width,
                checked((int)Math.Ceiling(x2)) + padding);

            int bottom = Math.Min(
                screenshot.Height,
                checked((int)Math.Ceiling(y2)) + padding);

            int width = checked(right - left);
            int height = checked(bottom - top);

            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);

            using Image<Rgb24> crop = screenshot.Clone(
                context => context.Crop(
                    new SixLabors.ImageSharp.Rectangle(
                        left,
                        top,
                        width,
                        height)));

            (string text, float score) = recognizer.Recognize(crop);

            float.IsFinite(score).Should().BeTrue(
                "the recognizer must return finite confidence values");

            candidateCount++;

            _output.WriteLine(
                $"CANDIDATE: Fixture={fixtureName}, " +
                $"Index={candidateCount}, " +
                $"V3Text=[{line.Text}], " +
                $"V6Text=[{text}], " +
                $"Score={score:G9}, " +
                $"Bounds={line.Bounds}, " +
                $"Crop=({left},{top},{width},{height})");

            if (string.Equals(
                    text,
                    targetText,
                    StringComparison.Ordinal) &&
                score > minimumConfidence)
            {
                matches.Add((line, text, score));
            }
        }

        matches.Should().ContainSingle(
            "the full screenshot must contain exactly one " +
            "high-confidence Traditional Chinese login label");

        var match = matches.Single();

        double centerX = match.Line.Bounds.Center.X;
        double centerY = match.Line.Bounds.Center.Y;

        double.IsFinite(centerX).Should().BeTrue();
        double.IsFinite(centerY).Should().BeTrue();

        centerX.Should().BeInRange(0, screenshot.Width - 1);
        centerY.Should().BeInRange(0, screenshot.Height - 1);

        centerX.Should().BeInRange(
            match.Line.Bounds.TopLeft.X,
            match.Line.Bounds.BottomRight.X);

        centerY.Should().BeInRange(
            match.Line.Bounds.TopLeft.Y,
            match.Line.Bounds.BottomRight.Y);

        match.Text.Should().Be(targetText);
        match.Score.Should().BeGreaterThan(minimumConfidence);

        _output.WriteLine(
            $"LOCALIZATION_SUCCESS: Fixture={fixtureName}, " +
            $"Candidates={candidateCount}, " +
            $"Matches={matches.Count}, " +
            $"Text=[{match.Text}], " +
            $"Confidence={match.Score:G9}, " +
            $"Bounds={match.Line.Bounds}, " +
            $"TextCenter=({centerX:G9},{centerY:G9}), " +
            "ClickSafety=NOT_VERIFIED");
    }


    [Fact]
    [Trait("Category", "PaddleOcrV6PaddingSensitivity")]
    public async Task DiagnosePaddingSensitivity_Fixture000008_ShouldCompareV6Recognition()
    {
        const string fixtureName = "ocr-diagnostic-000008.png";
        const string expectedText = "登入遊戲";
        const float acceptanceThreshold = 0.99f;

        int[] paddings = [4, 6, 8, 10];

        string modelDirectory = RequireDirectory("PADDLEOCR_V6_MODEL_DIR");
        string fixtureDirectory = RequireDirectory("PADDLEOCR_FIXTURE_DIR");

        string modelPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "inference.onnx"));

        string dictionaryPath = RequireFile(
            _fileSystem.Path.Combine(modelDirectory, "character_dict.json"));

        string fixturePath = RequireFile(
            _fileSystem.Path.Combine(fixtureDirectory, fixtureName));

        string detectionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DETECTION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DETECTION_MODEL is not configured."));

        string recognitionPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_RECOGNITION_MODEL")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_RECOGNITION_MODEL is not configured."));

        string legacyDictionaryPath = RequireFile(
            Environment.GetEnvironmentVariable("PADDLEOCR_DICTIONARY")
            ?? throw new InvalidOperationException(
                "PADDLEOCR_DICTIONARY is not configured."));

        byte[] screenshotBytes =
            await _fileSystem.File.ReadAllBytesAsync(fixturePath);

        using Image<Rgb24> screenshot = LoadImage(fixturePath);

        screenshot.Width.Should().Be(2340);
        screenshot.Height.Should().Be(1080);

        var options = new PaddleOcrOptions
        {
            DetectionModelPath = detectionPath,
            RecognitionModelPath = recognitionPath,
            DictionaryPath = legacyDictionaryPath
        };

        using var backend = new PaddleOcrOnnxBackend(options);

        IOCRUtilityService detector =
            new PaddleOcrUtilityService(backend);

        OcrResult detectionResult = await detector.RecognizeAsync(
            screenshotBytes,
            "zh-TW");

        detectionResult.Lines.Should().NotBeEmpty();

        // Use the known diagnostic area only to select the same V3 box.
        // All padding variants must share this single detection result.
        OcrTextLine[] candidates = detectionResult.Lines
            .Where(line =>
            {
                double x1 = line.Bounds.TopLeft.X;
                double y1 = line.Bounds.TopLeft.Y;
                double x2 = line.Bounds.BottomRight.X;
                double y2 = line.Bounds.BottomRight.Y;

                return double.IsFinite(x1) &&
                       double.IsFinite(y1) &&
                       double.IsFinite(x2) &&
                       double.IsFinite(y2) &&
                       x1 >= 0 &&
                       y1 >= 0 &&
                       x2 > x1 &&
                       y2 > y1 &&
                       x2 <= screenshot.Width &&
                       y2 <= screenshot.Height &&
                       line.Bounds.Center.X >= 900 &&
                       line.Bounds.Center.X <= 1400 &&
                       line.Bounds.Center.Y >= 700 &&
                       line.Bounds.Center.Y <= 820;
            })
            .ToArray();

        candidates.Should().ContainSingle(
            "fixture 000008 should have exactly one V3 text box " +
            "in the known diagnostic area");

        OcrTextLine candidate = candidates.Single();

        double x1 = candidate.Bounds.TopLeft.X;
        double y1 = candidate.Bounds.TopLeft.Y;
        double x2 = candidate.Bounds.BottomRight.X;
        double y2 = candidate.Bounds.BottomRight.Y;

        var preprocessor = new PaddleOcrV6ImagePreprocessor();

        using var inferenceEngine =
            new PaddleOcrV6OnnxInferenceEngine(modelPath);

        var decoder = new PaddleOcrV6CtcDecoder(dictionaryPath);

        using var recognizer = new PaddleOcrV6Recognizer(
            preprocessor,
            inferenceEngine,
            decoder);

        var results = new List<(
            int Padding,
            string Text,
            float Score,
            int Left,
            int Top,
            int Width,
            int Height)>();

        _output.WriteLine(
            $"PADDING_DIAGNOSTIC_START: Fixture={fixtureName}, " +
            $"Screenshot={screenshot.Width}x{screenshot.Height}, " +
            $"V3Text=[{candidate.Text}], " +
            $"V3Bounds={candidate.Bounds}");

        foreach (int padding in paddings)
        {
            int left = Math.Max(
                0,
                checked((int)Math.Floor(x1)) - padding);

            int top = Math.Max(
                0,
                checked((int)Math.Floor(y1)) - padding);

            int right = Math.Min(
                screenshot.Width,
                checked((int)Math.Ceiling(x2)) + padding);

            int bottom = Math.Min(
                screenshot.Height,
                checked((int)Math.Ceiling(y2)) + padding);

            int width = checked(right - left);
            int height = checked(bottom - top);

            width.Should().BeGreaterThan(0);
            height.Should().BeGreaterThan(0);

            left.Should().BeGreaterThanOrEqualTo(0);
            top.Should().BeGreaterThanOrEqualTo(0);

            ((long)left + width).Should().BeLessThanOrEqualTo(
                screenshot.Width);

            ((long)top + height).Should().BeLessThanOrEqualTo(
                screenshot.Height);

            using Image<Rgb24> crop = screenshot.Clone(
                context => context.Crop(
                    new SixLabors.ImageSharp.Rectangle(
                        left,
                        top,
                        width,
                        height)));

            (string text, float score) = recognizer.Recognize(crop);

            float.IsFinite(score).Should().BeTrue(
                $"padding {padding}px must return finite confidence");

            score.Should().BeInRange(0f, 1f);

            results.Add((
                padding,
                text,
                score,
                left,
                top,
                width,
                height));

            _output.WriteLine(
                $"PADDING_RESULT: Padding={padding}px, " +
                $"Crop=({left},{top},{width},{height}), " +
                $"Text=[{text}], " +
                $"Confidence={score:G9}, " +
                $"TextMatches={string.Equals(text, expectedText, StringComparison.Ordinal)}, " +
                $"Passes099={score > acceptanceThreshold && text == expectedText}");
        }

        results.Should().HaveCount(paddings.Length);

        var baseline = results.Single(result => result.Padding == 6);

        _output.WriteLine(
            $"BASELINE_6PX: Text=[{baseline.Text}], " +
            $"Confidence={baseline.Score:G9}");

        foreach (var result in results)
        {
            float delta = result.Score - baseline.Score;

            _output.WriteLine(
                $"PADDING_COMPARISON: Padding={result.Padding}px, " +
                $"Confidence={result.Score:G9}, " +
                $"DeltaFrom6px={delta:+0.000000000;-0.000000000;0.000000000}, " +
                $"Text=[{result.Text}]");
        }

        var correctResults = results
            .Where(result => string.Equals(
                result.Text,
                expectedText,
                StringComparison.Ordinal))
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Padding)
            .ToArray();

        if (correctResults.Length > 0)
        {
            var best = correctResults[0];

            _output.WriteLine(
                $"BEST_CORRECT_RESULT: Padding={best.Padding}px, " +
                $"Confidence={best.Score:G9}, " +
                $"ImprovementOver6px={best.Score - baseline.Score:G9}, " +
                $"Passes099={best.Score > acceptanceThreshold}");
        }
        else
        {
            _output.WriteLine(
                "BEST_CORRECT_RESULT: NONE");
        }

        // Diagnostic acceptance:
        // - Every padding variant must complete successfully.
        // - The known 6px baseline must still recognize the target.
        // - No assumption is made that a larger padding improves confidence.
        baseline.Text.Should().Be(
            expectedText,
            "the 6px baseline must reproduce the previously observed " +
            "Traditional Chinese recognition result");

        correctResults.Should().NotBeEmpty(
            "at least one padding must recognize the target text");

        _output.WriteLine(
            "PADDING_DIAGNOSTIC_COMPLETE: " +
            $"Variants={results.Count}, " +
            $"CorrectVariants={correctResults.Length}, " +
            "ClickSafety=NOT_VERIFIED");
    }

    private static DenseTensor<float> BuildDiagnosticTensor(
        Image<Rgb24> image,
        bool preserveAspectRatio,
        bool useBgr)
    {
        ArgumentNullException.ThrowIfNull(image);

        int resizedWidth = preserveAspectRatio
            ? Math.Clamp(
                (int)Math.Ceiling(48.0 * image.Width / image.Height),
                1,
                320)
            : 320;

        using Image<Rgb24> resized = image.Clone(
            context => context.Resize(new ResizeOptions
            {
                Size = new Size(resizedWidth, 48),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Triangle
            }));

        var tensor = new DenseTensor<float>(new[] { 1, 3, 48, 320 });

        resized.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 48; y++)
            {
                ReadOnlySpan<Rgb24> row = accessor.GetRowSpan(y);

                for (int x = 0; x < resizedWidth; x++)
                {
                    Rgb24 pixel = row[x];

                    byte channel0 = useBgr ? pixel.B : pixel.R;
                    byte channel1 = pixel.G;
                    byte channel2 = useBgr ? pixel.R : pixel.B;

                    tensor[0, 0, y, x] = channel0 / 127.5f - 1f;
                    tensor[0, 1, y, x] = channel1 / 127.5f - 1f;
                    tensor[0, 2, y, x] = channel2 / 127.5f - 1f;
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// Reads a required directory from an environment variable.
    /// </summary>
    private string RequireDirectory(string variableName)
    {
        string? path = Environment.GetEnvironmentVariable(variableName);

        path.Should().NotBeNullOrWhiteSpace(
            $"set {variableName} to the required asset directory");

        _fileSystem.Directory.Exists(path!).Should().BeTrue(
            $"{variableName} must point to an existing directory: '{path}'");

        return path!;
    }

    /// <summary>
    /// Validates the existence of a required model or fixture file.
    /// </summary>
    private string RequireFile(string path)
    {
        _fileSystem.File.Exists(path).Should().BeTrue(
            $"the required PaddleOCR v6 asset must exist at '{path}'");

        return path;
    }

    /// <summary>
    /// Loads an RGB image through the file-system abstraction.
    /// </summary>
    private Image<Rgb24> LoadImage(string path)
    {
        using Stream stream = _fileSystem.File.OpenRead(path);

        return Image.Load<Rgb24>(stream);
    }
}
