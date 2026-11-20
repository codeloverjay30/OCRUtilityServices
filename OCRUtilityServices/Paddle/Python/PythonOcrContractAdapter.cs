using Issue59.Contract;
using OCRUtilityServices.Models;
using CoordinateUtilityServices;

namespace OCRUtilityServices.Paddle.Python;

/// <summary>Converts corroborated original-image observations into non-authorizing OCR candidates.</summary>
public static class PythonOcrContractAdapter
{
    /// <summary>Rejects mismatched image dimensions and excludes ambiguous evidence.</summary>
    public static OcrResult Convert(string json, int imageWidth, int imageHeight)
    {
        OcrEnvelope envelope = OcrContractReader.Read(json);
        if (envelope.ImageWidth != imageWidth || envelope.ImageHeight != imageHeight)
            throw new InvalidDataException("Python OCR image dimensions mismatch.");
        var lines = envelope.ResolvedTextInstances
            .Where(x => x.LocalizationStatus == "corroborated" && x.CorroborationCount >= 2)
            .Select(x => new OcrTextLine(x.Text,
                new Rectangle(
                    new Point(x.Bounds.Left, x.Bounds.Top),
                    new Point(x.Bounds.Right, x.Bounds.Bottom))))
            .ToArray();
        return new OcrResult(string.Join(Environment.NewLine, lines.Select(x => x.Text)), lines);
    }
}
