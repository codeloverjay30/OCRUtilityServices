namespace OCRUtilityServices.Paddle.Python;

/// <summary>Configures a Python OCR process without enabling it by default.</summary>
public sealed record PythonOcrProcessOptions(
    string PythonExecutable,
    string ScriptPath,
    TimeSpan Timeout,
    int MaxOutputCharacters = 4_000_000);
