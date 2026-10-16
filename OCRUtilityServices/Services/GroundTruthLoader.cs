using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using OCRUtilityServices.Models;
using SixLabors.ImageSharp;
using Rectangle = CoordinateUtilityServices.Rectangle;

namespace OCRUtilityServices.Services;

/// <summary>
/// Loads only digest-pinned, reviewed annotations for an exact screenshot.
/// </summary>
public sealed class GroundTruthLoader : IGroundTruthLoader
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Initializes the loader with an abstract file system.</summary>
    public GroundTruthLoader(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <inheritdoc/>
    public async Task<ReviewedGroundTruth> LoadAsync(
        string annotationPath,
        string screenshotPath,
        string expectedAnnotationSha256,
        string expectedScreenshotSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(annotationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        ValidateDigest(expectedAnnotationSha256, nameof(expectedAnnotationSha256));
        ValidateDigest(expectedScreenshotSha256, nameof(expectedScreenshotSha256));

        cancellationToken.ThrowIfCancellationRequested();
        byte[] annotationBytes = await _fileSystem.File.ReadAllBytesAsync(
            annotationPath, cancellationToken);
        byte[] screenshotBytes = await _fileSystem.File.ReadAllBytesAsync(
            screenshotPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        VerifyDigest(annotationBytes, expectedAnnotationSha256, "Annotation");
        VerifyDigest(screenshotBytes, expectedScreenshotSha256, "Screenshot");

        AnnotationDocument document;
        try
        {
            document = JsonSerializer.Deserialize<AnnotationDocument>(
                annotationBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false })
                ?? throw new InvalidDataException("Ground truth JSON is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Ground truth JSON is invalid.", ex);
        }

        if (document.Status != "Reviewed")
            throw new InvalidDataException("Ground truth has not been reviewed.");
        if (string.IsNullOrWhiteSpace(document.Reviewer))
            throw new InvalidDataException("Ground truth reviewer is missing.");
        if (string.IsNullOrWhiteSpace(document.TargetText))
            throw new InvalidDataException("Ground truth target text is missing.");
        if (string.IsNullOrWhiteSpace(document.Fixture) ||
            document.Fixture != _fileSystem.Path.GetFileName(screenshotPath))
            throw new InvalidDataException("Ground truth fixture does not match screenshot.");
        if (document.TextBounds is null || document.ButtonBounds is null)
            throw new InvalidDataException("Ground truth bounds are missing.");

        int width;
        int height;
        try
        {
            using Image image = Image.Load(screenshotBytes);
            width = image.Width;
            height = image.Height;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("Ground truth screenshot cannot be decoded.", ex);
        }

        if (document.ImageWidth != width || document.ImageHeight != height)
            throw new InvalidDataException("Ground truth image dimensions do not match screenshot.");

        Rectangle textBounds = ToRectangle(document.TextBounds, width, height);
        Rectangle buttonBounds = ToRectangle(document.ButtonBounds, width, height);

        return new ReviewedGroundTruth(document.Fixture, document.TargetText,
            textBounds, buttonBounds, width, height);
    }

    /// <summary>Validates a SHA-256 digest represented as hexadecimal.</summary>
    private static void ValidateDigest(string digest, string argumentName)
    {
        if (digest is null || digest.Length != 64 ||
            !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 digest must be 64 hexadecimal characters.",
                argumentName);
    }

    /// <summary>
    /// Verifies an exact byte sequence against its approved SHA-256 digest.
    /// </summary>
    private static void VerifyDigest(
        byte[] bytes,
        string expected,
        string label)
    {
        byte[] expectedBytes;

        try
        {
            expectedBytes = Convert.FromHexString(expected);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                $"{label} SHA-256 digest is invalid.",
                ex);
        }

        if (expectedBytes.Length != 32)
        {
            throw new InvalidDataException(
                $"{label} SHA-256 digest is invalid.");
        }

        Span<byte> actualBytes = stackalloc byte[32];

        SHA256.HashData(bytes, actualBytes);

        if (!CryptographicOperations.FixedTimeEquals(
                actualBytes,
                expectedBytes))
        {
            throw new InvalidDataException(
                $"{label} SHA-256 does not match approved digest.");
        }
    }

    /// <summary>Converts and validates finite source-image rectangle coordinates.</summary>
    private static Rectangle ToRectangle(BoundsDocument bounds, int width, int height)
    {
        double right = bounds.X + bounds.Width;
        double bottom = bounds.Y + bounds.Height;
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            !double.IsFinite(right) || !double.IsFinite(bottom) ||
            bounds.X < 0 || bounds.Y < 0 ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            right > width || bottom > height)
            throw new InvalidDataException("Ground truth rectangle is invalid.");
        return Rectangle.FromXYWH(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    /// <summary>Defines the persisted annotation schema.</summary>
    private sealed record AnnotationDocument(
        string? Fixture, string? TargetText, string? Status, string? Reviewer,
        int ImageWidth, int ImageHeight,
        BoundsDocument? TextBounds, BoundsDocument? ButtonBounds);

    /// <summary>Defines persisted rectangle coordinates.</summary>
    private sealed record BoundsDocument(double X, double Y, double Width, double Height);
}
