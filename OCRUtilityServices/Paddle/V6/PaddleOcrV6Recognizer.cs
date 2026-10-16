using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>Coordinates cropped-image preprocessing, ONNX inference, and CTC decoding.</summary>
public sealed class PaddleOcrV6Recognizer : IPaddleOcrV6Recognizer
{
    private readonly IPaddleOcrV6ImagePreprocessor _preprocessor;
    private readonly IPaddleOcrV6InferenceEngine _inferenceEngine;
    private readonly IPaddleOcrV6CtcDecoder _decoder;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Creates a recognizer with injected collaborators.</summary>
    public PaddleOcrV6Recognizer(
        IPaddleOcrV6ImagePreprocessor preprocessor,
        IPaddleOcrV6InferenceEngine inferenceEngine,
        IPaddleOcrV6CtcDecoder decoder)
    {
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    /// <inheritdoc/>
    public (string Text, float Score) Recognize(
        Image<Rgb24> image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            var tensor = _preprocessor.Preprocess(image, cancellationToken);
            var (values, steps, classes) = _inferenceEngine.Run(tensor, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _decoder.Decode(values, steps, classes);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            // Injected dependencies are owned by the composition root / DI container.
            _disposed = true;
        }
    }
}
