namespace OCRUtilityServices.Paddle.V6;

/// <summary>
/// Defines CTC decoding for PaddleOCR v6 model outputs.
/// </summary>
public interface IPaddleOcrV6CtcDecoder
{
    /// <summary>
    /// Decodes CTC probabilities into recognized text and confidence.
    /// </summary>
    /// <param name="probabilities">The model output probabilities.</param>
    /// <param name="steps">The number of CTC time steps.</param>
    /// <param name="classes">The number of output classes.</param>
    /// <returns>The recognized text and confidence score.</returns>
    (string Text, float Score) Decode(
        ReadOnlySpan<float> probabilities,
        int steps,
        int classes);
}