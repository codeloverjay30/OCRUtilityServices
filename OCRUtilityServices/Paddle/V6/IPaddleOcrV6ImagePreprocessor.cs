using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Defines conversion of cropped RGB images into PaddleOCR v6 input tensors.
/// </summary>
public interface IPaddleOcrV6ImagePreprocessor
{
    /// <summary>
    /// Creates a normalized BGR tensor with shape [1,3,48,320].
    /// </summary>
    DenseTensor<float> Preprocess(Image<Rgb24> image, CancellationToken cancellationToken = default);
}
