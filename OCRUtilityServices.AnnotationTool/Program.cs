using System.IO.Abstractions;
using OCRUtilityServices.AnnotationTool;
using OCRUtilityServices.AnnotationTool.Services;
using OCRUtilityServices.Paddle;
using OCRUtilityServices.Services;

IFileSystem fileSystem = new FileSystem();

string inputDirectory = args[0];
string outputDirectory = args[1];

var options = new PaddleOcrOptions
{
    DetectionModelPath =
        Environment.GetEnvironmentVariable(
            "PADDLEOCR_DETECTION_MODEL")
        ?? throw new InvalidOperationException(
            "PADDLEOCR_DETECTION_MODEL is required."),

    RecognitionModelPath =
        Environment.GetEnvironmentVariable(
            "PADDLEOCR_RECOGNITION_MODEL")
        ?? throw new InvalidOperationException(
            "PADDLEOCR_RECOGNITION_MODEL is required."),

    DictionaryPath =
        Environment.GetEnvironmentVariable(
            "PADDLEOCR_DICTIONARY")
        ?? throw new InvalidOperationException(
            "PADDLEOCR_DICTIONARY is required.")
};

using var backend = new PaddleOcrOnnxBackend(options);

IOCRUtilityService ocr =
    new PaddleOcrUtilityService(backend);

IOcrTextMatcher matcher = new OcrTextMatcher();

var generator = new PaddleOcrAnnotationGenerator(
    fileSystem,
    ocr,
    matcher);

foreach (string input in fileSystem.Directory
    .EnumerateFiles(
        inputDirectory,
        "ocr-diagnostic-*.png",
        SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
{
    string name = fileSystem.Path.GetFileNameWithoutExtension(input);

    string output = fileSystem.Path.Combine(
        outputDirectory,
        name + ".json");

    try
    {
        await generator.GenerateAsync(
            input,
            output,
            "登入遊戲");

        Console.WriteLine($"Generated: {output}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"Failed: {name}: {ex.Message}");
    }
}