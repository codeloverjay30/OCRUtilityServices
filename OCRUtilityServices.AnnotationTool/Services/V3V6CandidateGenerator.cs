using System.Security.Cryptography;
using System.Text.Json;
using CoordinateUtilityServices;
using OCRUtilityServices.Models;
using OCRUtilityServices.Paddle.V6;
using OCRUtilityServices.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Rectangle = CoordinateUtilityServices.Rectangle;

namespace OCRUtilityServices.AnnotationTool.Services;

/// <summary>
/// Describes a proposed OCR rectangle in source-image pixel coordinates.
/// </summary>
public sealed record CandidateBounds(double X, double Y, double Width, double Height);

/// <summary>
/// Records a single V3-detected, V6-recognized OCR candidate.
/// </summary>
public sealed record V3V6Candidate(
    int Index,
    string RawText,
    float Confidence,
    CandidateBounds DetectionBounds,
    CandidateBounds RecognitionCropBounds,
    int CropPadding);

/// <summary>
/// Represents an unreviewed, screenshot-pinned OCR candidate report.
/// </summary>
public sealed record V3V6CandidateReport(
    string SchemaVersion,
    string Source,
    string Fixture,
    string ScreenshotSha256,
    string ExpectedText,
    string Status,
    bool ConfidenceAvailable,
    IReadOnlyList<V3V6Candidate> Candidates);

/// <summary>
/// Generates unreviewed OCR candidates using V3 region detection and V6 crop recognition.
/// </summary>
public sealed class V3V6CandidateGenerator
{
    private readonly IOCRUtilityService _detector;
    private readonly IPaddleOcrV6Recognizer _recognizer;

    /// <summary>
    /// Initializes the candidate generator with independently supplied OCR collaborators.
    /// </summary>
    public V3V6CandidateGenerator(
        IOCRUtilityService detector,
        IPaddleOcrV6Recognizer recognizer)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
    }

    /// <summary>
    /// Detects and recognizes OCR candidates without granting click authorization.
    /// </summary>
    public async Task<V3V6CandidateReport> GenerateAsync(
        byte[] screenshot,
        string fixture,
        string expectedText,
        Rectangle searchRegion,
        int cropPadding = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedText);
        if (screenshot.Length == 0)
            throw new ArgumentException("Screenshot buffer must not be empty.", nameof(screenshot));
        if (cropPadding < 0 || cropPadding > 64)
            throw new ArgumentOutOfRangeException(nameof(cropPadding), "Crop padding must be between 0 and 64.");
        cancellationToken.ThrowIfCancellationRequested();

        using Image<Rgb24> image = Image.Load<Rgb24>(screenshot);
        ValidateBounds(searchRegion, image.Width, image.Height, "Search region");
        OcrResult detections = await _detector.RecognizeRegionAsync(
            screenshot, searchRegion, "zh-TW", cancellationToken);

        if (detections?.Lines is null)
            throw new InvalidDataException("V3 detection result is missing.");

        var candidates = new List<V3V6Candidate>();
        for (int i = 0; i < detections.Lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OcrTextLine line = detections.Lines[i]
                ?? throw new InvalidDataException($"V3 detection line {i} is null.");
            Rectangle detected = line.Bounds;
            ValidateBounds(detected, image.Width, image.Height, $"V3 detection line {i}");

            int x1 = Math.Max(0, checked((int)Math.Floor(detected.TopLeft.X)) - cropPadding);
            int y1 = Math.Max(0, checked((int)Math.Floor(detected.TopLeft.Y)) - cropPadding);
            int x2 = Math.Min(image.Width, checked((int)Math.Ceiling(detected.BottomRight.X)) + cropPadding);
            int y2 = Math.Min(image.Height, checked((int)Math.Ceiling(detected.BottomRight.Y)) + cropPadding);
            if (x2 <= x1 || y2 <= y1)
                throw new InvalidDataException($"V6 crop for line {i} is empty.");

            using Image<Rgb24> crop = image.Clone(context =>
                context.Crop(new SixLabors.ImageSharp.Rectangle(x1, y1, x2 - x1, y2 - y1)));
            (string text, float score) = _recognizer.Recognize(crop, cancellationToken);
            if (text is null || !float.IsFinite(score) || score < 0 || score > 1)
                throw new InvalidDataException($"V6 recognition result for line {i} is invalid.");

            string normalized = Normalize(text);
            if (!string.Equals(normalized, Normalize(expectedText), StringComparison.Ordinal))
                continue;

            candidates.Add(new V3V6Candidate(
                i, text, score,
                ToBounds(detected),
                new CandidateBounds(x1, y1, x2 - x1, y2 - y1),
                cropPadding));
        }

        return new V3V6CandidateReport(
            "1.0",
            "PaddleOCR-V3-Detection-V6-Recognition",
            fixture,
            Convert.ToHexString(SHA256.HashData(screenshot)).ToLowerInvariant(),
            expectedText,
            "PendingReview",
            true,
            candidates);
    }

    /// <summary>
    /// Serializes a pending-review report without changing its approval status.
    /// </summary>
    public static string Serialize(V3V6CandidateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Status != "PendingReview")
            throw new InvalidOperationException("Candidate report must remain PendingReview.");
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Validates that a rectangle is finite, nonempty, and inside its source image.
    /// </summary>
    private static void ValidateBounds(Rectangle bounds, int width, int height, string description)
    {
        double x = bounds.TopLeft.X;
        double y = bounds.TopLeft.Y;
        double right = bounds.BottomRight.X;
        double bottom = bounds.BottomRight.Y;
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(right) || !double.IsFinite(bottom) ||
            x < 0 || y < 0 || right <= x || bottom <= y ||
            right > width || bottom > height)
            throw new InvalidDataException($"{description} has invalid source-image geometry.");
    }

    /// <summary>
    /// Removes whitespace without altering the order or identity of characters.
    /// </summary>
    private static string Normalize(string text)
    {
        ReadOnlySpan<char> span = text.AsSpan();
        var builder = new System.Text.StringBuilder(span.Length);
        foreach (char c in span)
            if (!char.IsWhiteSpace(c))
                builder.Append(c);
        return builder.ToString();
    }

    /// <summary>
    /// Converts the source-image rectangle to a JSON-friendly bounds record.
    /// </summary>
    private static CandidateBounds ToBounds(Rectangle rectangle)
        => new(rectangle.TopLeft.X, rectangle.TopLeft.Y, rectangle.Width, rectangle.Height);
}
