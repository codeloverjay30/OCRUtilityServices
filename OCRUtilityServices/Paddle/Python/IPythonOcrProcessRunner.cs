namespace OCRUtilityServices.Paddle.Python;

/// <summary>Runs an isolated Python OCR process against an image file.</summary>
public interface IPythonOcrProcessRunner
{
    /// <summary>Returns a JSON envelope from standard output or fails closed.</summary>
    Task<string> RunAsync(string imagePath, CancellationToken cancellationToken);
}
