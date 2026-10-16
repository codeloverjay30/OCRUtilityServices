using System.IO.Abstractions;
using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Paddle;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace OCRUtilityServices.IntegrationTests;

/// <summary>
/// Compares the original recognition assets against a candidate Traditional Chinese model and its matching dictionary.
/// </summary>
public sealed class PaddleOcrTraditionalModelPairIntegrationTests
{
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes the diagnostic integration test.
    /// </summary>
    public PaddleOcrTraditionalModelPairIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "PaddleOcrTraditionalPair")]
    public async Task CandidateRecognitionPair_Fixture000007_ShouldSupportAndRecognizeTraditionalTarget()
    {
        const string expected = "登入遊戲";
        const string inspected = "登入遊戲游虔";
        const int left = 1104;
        const int top = 742;
        const int width = 127;
        const int height = 25;

        string detector = RequireFile("PADDLEOCR_DETECTION_MODEL");
        string baselineRecognizer = RequireFile("PADDLEOCR_RECOGNITION_MODEL");
        string baselineDictionary = RequireFile("PADDLEOCR_DICTIONARY");
        string candidateRecognizer = RequireFile("PADDLEOCR_TRAD_RECOGNITION_MODEL");
        string candidateDictionary = RequireFile("PADDLEOCR_TRAD_DICTIONARY");
        string fixtureDirectory = RequireDirectory("PADDLEOCR_FIXTURE_DIR");
        string fixture = _fileSystem.Path.Combine(fixtureDirectory, "ocr-diagnostic-000007.png");
        _fileSystem.File.Exists(fixture).Should().BeTrue("the fixed OCR fixture must exist");

        string baselineModelHash = Sha256(baselineRecognizer);
        string baselineDictionaryHash = Sha256(baselineDictionary);
        string candidateModelHash = Sha256(candidateRecognizer);
        string candidateDictionaryHash = Sha256(candidateDictionary);

        _output.WriteLine($"BaselineModelSHA256={baselineModelHash}");
        _output.WriteLine($"BaselineDictionarySHA256={baselineDictionaryHash}");
        _output.WriteLine($"CandidateModelSHA256={candidateModelHash}");
        _output.WriteLine($"CandidateDictionarySHA256={candidateDictionaryHash}");

        byte[] bytes = await _fileSystem.File.ReadAllBytesAsync(fixture);
        using Image<Rgb24> image = Image.Load<Rgb24>(bytes);
        ((long)left + width).Should().BeLessThanOrEqualTo(image.Width);
        ((long)top + height).Should().BeLessThanOrEqualTo(image.Height);

        using (var baseline = new PaddleOcrOnnxBackend(
            CreateOptions(detector, baselineRecognizer, baselineDictionary)))
        {
            PaddleOcrCtcDiagnosticResult result = baseline.DiagnoseCtcTopK(
                image, left, top, width, height, inspected, topK: 5);

            PrintDiagnostic("Baseline", result);
            result.DecodedText.Should().Be("登入游虔",
                "the baseline must reproduce the original 2 px crop experiment");
            result.DictionaryCoverage
                .Where(entry => entry.Character is "遊" or "戲")
                .Should().OnlyContain(entry => entry.ClassIndices.Count == 0,
                    "the original simplified Chinese dictionary lacks the two Traditional Chinese characters");
        }

        // Avoid treating the original assets as a new model/dictionary experiment.
        candidateModelHash.Should().NotBe(baselineModelHash,
            "the candidate recognition model must differ from the baseline");
        candidateDictionaryHash.Should().NotBe(baselineDictionaryHash,
            "the candidate dictionary must differ from the baseline");

        using var candidate = new PaddleOcrOnnxBackend(
            CreateOptions(detector, candidateRecognizer, candidateDictionary));

        // DiagnoseCtcTopK validates ONNX class count == dictionary length + 2.
        // This proves shape compatibility, NOT provenance or character order.
        PaddleOcrCtcDiagnosticResult candidateResult = candidate.DiagnoseCtcTopK(
            image, left, top, width, height, inspected, topK: 5);

        PrintDiagnostic("Candidate", candidateResult);
        candidateResult.DictionaryCoverage
            .Where(entry => expected.Contains(entry.Character, StringComparison.Ordinal))
            .Should().HaveCount(4);
        candidateResult.DictionaryCoverage
            .Where(entry => expected.Contains(entry.Character, StringComparison.Ordinal))
            .Should().OnlyContain(entry => entry.ClassIndices.Count > 0,
                "the candidate dictionary must contain all four Traditional Chinese target characters");
        candidateResult.DecodedText.Should().Be(expected,
            "the model and dictionary pair must recognize the fixed screenshot before production adoption");
    }

    /// <summary>
    /// Builds an ONNX backend configuration with unchanged preprocessing dimensions.
    /// </summary>
    private static PaddleOcrOptions CreateOptions(string detector, string recognizer, string dictionary)
        => new()
        {
            DetectionModelPath = detector,
            RecognitionModelPath = recognizer,
            DictionaryPath = dictionary,
            DetectionSize = 960,
            RecognitionWidth = 320,
            RecognitionHeight = 48
        };

    /// <summary>
    /// Reads and validates an explicitly configured file path.
    /// </summary>
    private string RequireFile(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        value.Should().NotBeNullOrWhiteSpace($"configure {variable} before running this experiment");
        _fileSystem.File.Exists(value!).Should().BeTrue($"{variable} must point to an existing file");
        return value!;
    }

    /// <summary>
    /// Validates a required directory from an environment variable.
    /// </summary>
    private string RequireDirectory(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        value.Should().NotBeNullOrWhiteSpace(
            $"configure {variable} before running this experiment");

        _fileSystem.Directory.Exists(value!).Should().BeTrue(
            $"{variable} must point to an existing directory");

        return value!;
    }

    /// <summary>
    /// Computes the SHA-256 hash of a model or dictionary file.
    /// </summary>
    private string Sha256(string path)
    {
        using Stream stream = _fileSystem.File.OpenRead(path);

        return Convert.ToHexString(
            SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Writes CTC recognition diagnostics to the test output.
    /// </summary>
    private void PrintDiagnostic(
        string name,
        PaddleOcrCtcDiagnosticResult result)
    {
        _output.WriteLine(
            $"{name}: Text=[{result.DecodedText}], " +
            $"Classes={result.ClassCount}, " +
            $"Steps={result.TimeStepCount}");

        foreach (PaddleOcrCtcDictionaryEntry entry
                 in result.DictionaryCoverage)
        {
            _output.WriteLine(
                $"{name}: Character=[{entry.Character}], " +
                $"Indices=[{string.Join(",", entry.ClassIndices)}]");
        }

        foreach (PaddleOcrCtcTimeStep step in result.TimeSteps)
        {
            string candidates = string.Join(
                " | ",
                step.Candidates.Select(candidate =>
                    $"{candidate.ClassIndex}:{candidate.Label}:{candidate.Score:G9}"));

            _output.WriteLine(
                $"{name}: T={step.TimeStep:D3} {candidates}");
        }
    }
}
