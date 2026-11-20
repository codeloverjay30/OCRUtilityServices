using System.Text.Json;
using System.Text.Json.Serialization;

namespace Issue59.Contract;

/// <summary>Represents a rectangle in original-image pixel coordinates.</summary>
public sealed record OcrBounds(double Left, double Top, double Right, double Bottom);

/// <summary>Represents an OCR observation with explicit localization provenance.</summary>
public sealed record OcrInstance(
    string Text,
    double Confidence,
    OcrBounds Bounds,
    string Source,
    string LocalizationStatus,
    int CorroborationCount);

/// <summary>Represents the two distinct OCR evidence layers.</summary>
public sealed record OcrEnvelope(
    string SchemaVersion,
    int ImageWidth,
    int ImageHeight,
    string CoordinateSpace,
    IReadOnlyList<OcrInstance> ResolvedTextInstances,
    IReadOnlyList<OcrInstance> UnresolvedMergedBoxes);

/// <summary>Rejects invalid or incompatible OCR transport envelopes before localization.</summary>
public static class OcrContractReader
{
    /// <summary>Deserializes a versioned OCR response and validates every observation.</summary>
    public static OcrEnvelope Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        OcrEnvelope? value;
        try
        {
            value = JsonSerializer.Deserialize<OcrEnvelope>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.Strict
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid OCR JSON payload.", ex);
        }

        if (value is null || value.SchemaVersion != "1.0.0")
            throw new InvalidDataException("Unsupported OCR schema version.");
        if (value.ImageWidth <= 0 || value.ImageHeight <= 0 ||
            value.CoordinateSpace != "original_image_pixels")
            throw new InvalidDataException("Invalid OCR image coordinate space.");
        if (value.ResolvedTextInstances is null || value.UnresolvedMergedBoxes is null)
            throw new InvalidDataException("Missing OCR evidence layers.");

        foreach (var item in value.ResolvedTextInstances)
        {
            Validate(item, value.ImageWidth, value.ImageHeight);
            if (item.LocalizationStatus is not ("original_detection" or "corroborated"))
                throw new InvalidDataException("Invalid resolved localization status.");
            if (item.LocalizationStatus == "corroborated" && item.CorroborationCount < 2)
                throw new InvalidDataException("Insufficient corroboration evidence.");
        }
        foreach (var item in value.UnresolvedMergedBoxes)
        {
            Validate(item, value.ImageWidth, value.ImageHeight);
            if (item.LocalizationStatus != "ambiguous_merged_text")
                throw new InvalidDataException("Invalid unresolved localization status.");
        }
        return value;
    }

    /// <summary>Validates confidence, source, text and original-image geometry.</summary>
    private static void Validate(OcrInstance? item, int width, int height)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Text) ||
            string.IsNullOrWhiteSpace(item.Source) || item.Bounds is null ||
            !double.IsFinite(item.Confidence) ||
            item.Confidence < 0 || item.Confidence > 1 ||
            !double.IsFinite(item.Bounds.Left) || !double.IsFinite(item.Bounds.Top) ||
            !double.IsFinite(item.Bounds.Right) || !double.IsFinite(item.Bounds.Bottom) ||
            item.Bounds.Left < 0 || item.Bounds.Top < 0 ||
            item.Bounds.Right > width || item.Bounds.Bottom > height ||
            item.Bounds.Left >= item.Bounds.Right || item.Bounds.Top >= item.Bounds.Bottom ||
            item.CorroborationCount < 0)
            throw new InvalidDataException("Invalid OCR observation or original-image bounds.");
    }
}

/// <summary>Provides conservative text localization without authorizing a click.</summary>
public static class OcrTextLocator
{
    /// <summary>Returns one exact corroborated match, or refuses ambiguous and uncorroborated matches.</summary>
    public static OcrInstance FindUniqueCorroborated(OcrEnvelope envelope, string text)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var matches = envelope.ResolvedTextInstances
            .Where(x => x.LocalizationStatus == "corroborated" &&
                        x.CorroborationCount >= 2 &&
                        string.Equals(x.Text, text, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("OCR target is missing or ambiguous.");
        return matches[0];
    }
}
