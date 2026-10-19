using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using CoordinateUtilityServices;

namespace OCRUtilityServices.Services;

/// <summary>
/// Binds a trusted review digest to independently verified ground-truth evidence.
/// This service calculates coordinates only and never performs device input.
/// </summary>
public sealed class ReviewIntegrityAuthorizationService : IReviewIntegrityAuthorizationService
{
    private readonly IFileSystem _fileSystem;
    private readonly IGroundTruthLoader _loader;
    private readonly IReviewAwareGroundedClickAuthorizationService _reviewGate;

    /// <summary>
    /// Initializes the evidence verification boundary.
    /// </summary>
    public ReviewIntegrityAuthorizationService(
        IFileSystem fileSystem,
        IGroundTruthLoader loader,
        IReviewAwareGroundedClickAuthorizationService reviewGate)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _reviewGate = reviewGate ?? throw new ArgumentNullException(nameof(reviewGate));
    }

    /// <inheritdoc/>
    public async Task<(double X, double Y)> AuthorizeAsync(
        string recognizedText,
        float confidence,
        Rectangle detectedTextBounds,
        string reviewPath,
        string annotationPath,
        string screenshotPath,
        string trustedReviewSha256,
        string trustedAnnotationSha256,
        string trustedScreenshotSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);

        byte[] expectedReview = ParseDigest(trustedReviewSha256, nameof(trustedReviewSha256));
        byte[] expectedAnnotation = ParseDigest(trustedAnnotationSha256, nameof(trustedAnnotationSha256));
        byte[] expectedScreenshot = ParseDigest(trustedScreenshotSha256, nameof(trustedScreenshotSha256));

        cancellationToken.ThrowIfCancellationRequested();
        byte[] reviewBytes = await _fileSystem.File.ReadAllBytesAsync(reviewPath, cancellationToken);
        Span<byte> actualReview = stackalloc byte[32];
        SHA256.HashData(reviewBytes, actualReview);
        if (!CryptographicOperations.FixedTimeEquals(actualReview, expectedReview))
            throw new InvalidDataException("Review SHA-256 does not match trusted digest.");

        using JsonDocument document = ParseReview(reviewBytes);
        JsonElement review = document.RootElement;
        if (review.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Review JSON root must be an object.");

        // Bind the review metadata to the exact evidence paths and trusted digests.
        RequireString(review, "AnnotationFile", _fileSystem.Path.GetFileName(annotationPath));
        RequireString(review, "AnnotationSha256", trustedAnnotationSha256, ignoreCase: true);
        RequireString(review, "ScreenshotSha256", trustedScreenshotSha256, ignoreCase: true);

        // Existing loader independently hashes both evidence files, validates
        // the decoded screenshot dimensions and returns verified geometry.
        var truth = await _loader.LoadAsync(
            annotationPath, screenshotPath,
            trustedAnnotationSha256, trustedScreenshotSha256, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return _reviewGate.Authorize(
            recognizedText, confidence, detectedTextBounds, truth, review);
    }

    /// <summary>
    /// Validates and decodes a trusted SHA-256 digest.
    /// </summary>
    private static byte[] ParseDigest(string value, string argumentName)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "Trusted SHA-256 digest must contain 64 hexadecimal characters.", argumentName);
        return Convert.FromHexString(value);
    }

    /// <summary>
    /// Parses the digest-verified review and wraps malformed JSON.
    /// </summary>
    private static JsonDocument ParseReview(byte[] bytes)
    {
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Review JSON is invalid.", ex);
        }
    }

    /// <summary>
    /// Rejects missing or mismatched evidence bindings.
    /// </summary>
    private static void RequireString(
        JsonElement review, string name, string expected, bool ignoreCase = false)
    {
        if (!review.TryGetProperty(name, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String ||
            !string.Equals(element.GetString(), expected,
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Review {name} does not match trusted evidence.");
    }
}
