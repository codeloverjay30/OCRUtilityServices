using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Owns and synchronizes a PaddleOCR v6 ONNX Runtime inference session.
/// </summary>
public sealed class PaddleOcrV6OnnxInferenceEngine : IPaddleOcrV6InferenceEngine
{
    private readonly InferenceSession _session;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Loads a PaddleOCR v6 ONNX model and validates its tensor contract.</summary>
    public PaddleOcrV6OnnxInferenceEngine(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _session = new InferenceSession(modelPath);
        try
        {
            if (_session.InputMetadata.Count != 1 ||
                !_session.InputMetadata.TryGetValue("x", out var input) ||
                input.Dimensions.Length != 4 || input.Dimensions[1] != 3 ||
                input.Dimensions[2] != 48 ||
                _session.OutputMetadata.Count != 1 ||
                !_session.OutputMetadata.TryGetValue("fetch_name_0", out var output) ||
                output.Dimensions.Length != 3 || output.Dimensions[2] != 18710)
                throw new NotSupportedException("Unexpected PaddleOCR v6 ONNX contract.");
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public (float[] Values, int Steps, int Classes) Run(
        DenseTensor<float> input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Dimensions.Length != 4 || input.Dimensions[0] != 1 ||
            input.Dimensions[1] != 3 || input.Dimensions[2] != 48 ||
            input.Dimensions[3] != 320)
            throw new ArgumentException("Expected PaddleOCR v6 input [1,3,48,320].", nameof(input));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            using var results = _session.Run(
                new[] { NamedOnnxValue.CreateFromTensor("x", input) });
            var result = results.Single().AsTensor<float>();
            if (result.Dimensions.Length != 3 || result.Dimensions[0] != 1 ||
                result.Dimensions[1] <= 0 || result.Dimensions[2] != 18710)
                throw new InvalidDataException("Unexpected PaddleOCR v6 output shape.");
            float[] values = result.ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return (values, result.Dimensions[1], result.Dimensions[2]);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
