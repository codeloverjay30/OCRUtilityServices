using System.IO.Abstractions;
using System.Security.Cryptography;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle;
using OCRUtilityServices.Paddle.V6;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OCRUtilityServices.AnnotationTool.Services;
using CoordinateRectangle = CoordinateUtilityServices.Rectangle;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: OCRUtilityServices.AnnotationTool <input-png> <output-json>");
    return 2;
}

IFileSystem fileSystem = new FileSystem();
string imagePath = fileSystem.Path.GetFullPath(args[0]);
string outputPath = fileSystem.Path.GetFullPath(args[1]);

if (!fileSystem.File.Exists(imagePath))
{
    Console.Error.WriteLine($"Screenshot not found: {imagePath}");
    return 2;
}

if (fileSystem.File.Exists(outputPath))
{
    Console.Error.WriteLine($"Refusing to overwrite existing candidate: {outputPath}");
    return 2;
}

string detectionModel = Environment.GetEnvironmentVariable("PADDLEOCR_DETECTION_MODEL")
    ?? throw new InvalidOperationException("PADDLEOCR_DETECTION_MODEL is required.");
string recognitionModel = Environment.GetEnvironmentVariable("PADDLEOCR_RECOGNITION_MODEL")
    ?? throw new InvalidOperationException("PADDLEOCR_RECOGNITION_MODEL is required.");
string dictionary = Environment.GetEnvironmentVariable("PADDLEOCR_DICTIONARY")
    ?? throw new InvalidOperationException("PADDLEOCR_DICTIONARY is required.");

var options = new PaddleOcrOptions
{
    DetectionModelPath = detectionModel,
    RecognitionModelPath = recognitionModel,
    DictionaryPath = dictionary
};

byte[] screenshot = await fileSystem.File.ReadAllBytesAsync(imagePath);
string screenshotSha256 = Convert.ToHexString(SHA256.HashData(screenshot)).ToLowerInvariant();

using var backend = new PaddleOcrOnnxBackend(options);
IOCRUtilityService ocr = new PaddleOcrUtilityService(backend);
OcrResult result = await ocr.RecognizeAsync(screenshot, "zh-TW");

// Diagnostic ROI only; not an approved text or click rectangle.
var taskSearchRegion = CoordinateRectangle.FromXYWH(
    450,
    720,
    300,
    260);

OcrResult roiResult = await ocr.RecognizeRegionAsync(
    screenshot,
    taskSearchRegion,
    "zh-TW");

Console.WriteLine("========== TASK ROI DIAGNOSTICS ==========");

foreach (OcrTextLine line in roiResult.Lines)
{
    Console.WriteLine(
        $"Text='{line.Text}', Bounds={line.Bounds}");
}

Console.WriteLine("========== END TASK ROI DIAGNOSTICS ==========");

string v6Directory = Environment.GetEnvironmentVariable("PADDLEOCR_V6_MODEL_DIR")
    ?? throw new InvalidOperationException(
        "PADDLEOCR_V6_MODEL_DIR is required.");

string v6ModelPath = fileSystem.Path.Combine(v6Directory, "inference.onnx");
string v6DictionaryPath = fileSystem.Path.Combine(
    v6Directory, "character_dict.json");

if (!fileSystem.File.Exists(v6ModelPath) ||
    !fileSystem.File.Exists(v6DictionaryPath))
{
    throw new FileNotFoundException(
        "PaddleOCR v6 model or dictionary was not found.");
}

using var sourceImage = Image.Load<Rgb24>(screenshot);
using var v6Engine = new PaddleOcrV6OnnxInferenceEngine(v6ModelPath);
var v6Decoder = new PaddleOcrV6CtcDecoder(v6DictionaryPath);
using var v6Recognizer = new PaddleOcrV6Recognizer(
    new PaddleOcrV6ImagePreprocessor(),
    v6Engine,
    v6Decoder);

Console.WriteLine("========== V6 ROI RECOGNITION DIAGNOSTICS ==========");

foreach (var (line, index) in roiResult.Lines.Select((value, i) => (value, i)))
{
    var bounds = line.Bounds;

    double left = bounds.TopLeft.X;
    double top = bounds.TopLeft.Y;
    double right = bounds.BottomRight.X;
    double bottom = bounds.BottomRight.Y;

    if (!double.IsFinite(left) ||
        !double.IsFinite(top) ||
        !double.IsFinite(right) ||
        !double.IsFinite(bottom) ||
        left < 0 ||
        top < 0 ||
        right <= left ||
        bottom <= top ||
        right > sourceImage.Width ||
        bottom > sourceImage.Height)
    {
        throw new InvalidDataException(
            $"Invalid OCR candidate geometry at index {index}.");
    }

    foreach (int padding in new[] { 0, 2, 4, 8 })
    {
        int x1 = Math.Max(0, checked((int)Math.Floor(left)) - padding);
        int y1 = Math.Max(0, checked((int)Math.Floor(top)) - padding);
        int x2 = Math.Min(sourceImage.Width, checked((int)Math.Ceiling(right)) + padding);
        int y2 = Math.Min(sourceImage.Height, checked((int)Math.Ceiling(bottom)) + padding);

        using Image<Rgb24> crop = sourceImage.Clone(context =>
            context.Crop(new SixLabors.ImageSharp.Rectangle(
                x1,
                y1,
                checked(x2 - x1),
                checked(y2 - y1))));

        (string recognizedText, float score) = v6Recognizer.Recognize(crop);

        if (!float.IsFinite(score))
        {
            throw new InvalidDataException(
                $"Non-finite V6 recognition score at index {index}.");
        }

        Console.WriteLine(
            $"Candidate={index}, Padding={padding}, " +
            $"V3Text='{line.Text}', " +
            $"V6Text='{recognizedText}', " +
            $"V6Score={score:G9}, " +
            $"Crop=({x1},{y1},{x2 - x1},{y2 - y1})");
    }
}

Console.WriteLine("========== END V6 ROI DIAGNOSTICS ==========");

Console.WriteLine();
Console.WriteLine("========== OCR RAW DIAGNOSTICS ==========");
Console.WriteLine($"Total OCR lines: {result.Lines.Count}");
Console.WriteLine($"Full recognized text: {result.Text}");
Console.WriteLine();

foreach (var (line, index) in result.Lines.Select((line, index) => (line, index)))
{
    string normalizedText = string.Concat(
        line.Text.Where(c => !char.IsWhiteSpace(c)));

    var bounds = line.Bounds;

    Console.WriteLine(
        $"[{index:D3}] " +
        $"RawText='{line.Text}' | " +
        $"NormalizedText='{normalizedText}' | " +
        $"Bounds=(X={bounds.TopLeft.X:F1}, " +
        $"Y={bounds.TopLeft.Y:F1}, " +
        $"W={bounds.Width:F1}, " +
        $"H={bounds.Height:F1}) | " +
        $"Words={line.Words.Count}");
}

Console.WriteLine("========== END OCR DIAGNOSTICS ==========");
Console.WriteLine();

Console.WriteLine("========== TASK TEXT DIAGNOSTICS ==========");

foreach (var (line, index) in result.Lines.Select((line, index) => (line, index)))
{
    string normalized = string.Concat(
        line.Text.Where(c => !char.IsWhiteSpace(c)));

    bool exactMatch = string.Equals(
        normalized,
        "任務",
        StringComparison.Ordinal);

    bool containsTraditional = normalized.Contains(
        "任務",
        StringComparison.Ordinal);

    bool containsSimplified = normalized.Contains(
        "任务",
        StringComparison.Ordinal);

    bool containsEitherCharacter =
        normalized.Contains('任') ||
        normalized.Contains('務') ||
        normalized.Contains('务');

    if (!containsEitherCharacter)
    {
        continue;
    }

    Console.WriteLine(
        $"[{index:D3}] " +
        $"Raw='{line.Text}' | " +
        $"Exact={exactMatch} | " +
        $"ContainsTraditional={containsTraditional} | " +
        $"ContainsSimplified={containsSimplified} | " +
        $"Bounds={line.Bounds}");
}

Console.WriteLine("========== END TASK DIAGNOSTICS ==========");

// The formal candidate report must use V3 ROI detection + V6 recognition.
// Diagnostic full-screen V3 text must never determine candidate acceptance.
var generator = new V3V6CandidateGenerator(ocr, v6Recognizer);
V3V6CandidateReport report = await generator.GenerateAsync(
    screenshot,
    fileSystem.Path.GetFileName(imagePath),
    "任務",
    taskSearchRegion,
    cropPadding: 8);

if (!string.Equals(report.ScreenshotSha256, screenshotSha256, StringComparison.Ordinal))
{
    throw new InvalidDataException("Candidate report screenshot SHA-256 does not match.");
}

string json = V3V6CandidateGenerator.Serialize(report);
string? parent = fileSystem.Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(parent))
{
    fileSystem.Directory.CreateDirectory(parent);
}

string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    await fileSystem.File.WriteAllTextAsync(temporaryPath, json);
    fileSystem.File.Move(temporaryPath, outputPath);
}
finally
{
    if (fileSystem.File.Exists(temporaryPath))
    {
        fileSystem.File.Delete(temporaryPath);
    }
}

Console.WriteLine($"Screenshot SHA-256: {report.ScreenshotSha256}");
Console.WriteLine($"Matching OCR candidates: {report.Candidates.Count}");
foreach (V3V6Candidate candidate in report.Candidates)
{
    Console.WriteLine(
        $"#{candidate.Index}: '{candidate.RawText}', " +
        $"score={candidate.Confidence:G9}, " +
        $"detection={candidate.DetectionBounds}, " +
        $"crop={candidate.RecognitionCropBounds}, " +
        $"padding={candidate.CropPadding}");
}
Console.WriteLine($"Saved unreviewed OCR candidates: {outputPath}");

// Exit code 0 means exactly one proposed candidate was produced.
// It does NOT authorize a click or mark ground truth as reviewed.
return report.Candidates.Count == 1 ? 0 : 3;
