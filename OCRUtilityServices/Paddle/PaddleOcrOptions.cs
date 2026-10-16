namespace OCRUtilityServices.Paddle;

/// <summary>
/// Configures the ONNX models and CTC character dictionary.
/// </summary>
public sealed class PaddleOcrOptions
{
    /// <summary>Gets the path to the DB detection ONNX model.</summary>
    public required string DetectionModelPath { get; init; }
    /// <summary>Gets the path to the CTC recognition ONNX model.</summary>
    public required string RecognitionModelPath { get; init; }
    /// <summary>Gets the path to the recognition character dictionary.</summary>
    public required string DictionaryPath { get; init; }
    /// <summary>Gets the detection input size; this candidate requires a square static model.</summary>
    public int DetectionSize { get; init; } = 960;
    /// <summary>Gets the fixed recognition input height.</summary>
    public int RecognitionHeight { get; init; } = 48;
    /// <summary>Gets the fixed recognition input width.</summary>
    public int RecognitionWidth { get; init; } = 320;
    /// <summary>Gets the DB probability threshold.</summary>
    public float DetectionThreshold { get; init; } = 0.3f;
    /// <summary>Gets the minimum accepted connected component area in output pixels.</summary>
    public int MinimumComponentArea { get; init; } = 8;
}
