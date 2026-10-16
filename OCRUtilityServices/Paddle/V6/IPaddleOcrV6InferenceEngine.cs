using Microsoft.ML.OnnxRuntime.Tensors;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Defines ONNX inference for preprocessed PaddleOCR v6 recognition tensors.
/// </summary>
public interface IPaddleOcrV6InferenceEngine : IDisposable
{
    /// <summary>
    /// Runs one recognition tensor and returns its detached CTC output.
    /// </summary>
    (float[] Values, int Steps, int Classes) Run(
        DenseTensor<float> input,
        CancellationToken cancellationToken = default);
}
