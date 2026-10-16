using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Creates aspect-preserving, right-padded PaddleOCR v6 BGR input tensors.
/// </summary>
public sealed class PaddleOcrV6ImagePreprocessor : IPaddleOcrV6ImagePreprocessor
{
    /// <inheritdoc/>
    public DenseTensor<float> Preprocess(Image<Rgb24> image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Width <= 0 || image.Height <= 0)
            throw new ArgumentException("OCR image dimensions must be positive.", nameof(image));

        int width = Math.Clamp((int)Math.Ceiling(48.0 * image.Width / image.Height), 1, 320);
        using Image<Rgb24> resized = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(width, 48),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.Triangle
        }));
        var tensor = new DenseTensor<float>(new[] { 1, 3, 48, 320 });
        resized.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 48; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    Rgb24 p = row[x];
                    tensor[0, 0, y, x] = p.B / 127.5f - 1f;
                    tensor[0, 1, y, x] = p.G / 127.5f - 1f;
                    tensor[0, 2, y, x] = p.R / 127.5f - 1f;
                }
            }
        });
        return tensor;
    }
}
