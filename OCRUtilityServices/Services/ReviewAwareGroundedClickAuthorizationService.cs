using System.Text.Json;
using CoordinateUtilityServices;
using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Evaluates explicit review approval before delegating to OCR geometry authorization.
/// </summary>
public sealed class ReviewAwareGroundedClickAuthorizationService : IReviewAwareGroundedClickAuthorizationService
{
    private readonly IGroundedClickAuthorizationService _geometryAuthorization;

    /// <summary>
    /// Initializes the review-aware authorization boundary.
    /// </summary>
    public ReviewAwareGroundedClickAuthorizationService(
        IGroundedClickAuthorizationService geometryAuthorization)
    {
        _geometryAuthorization = geometryAuthorization
            ?? throw new ArgumentNullException(nameof(geometryAuthorization));
    }

    /// <inheritdoc/>
    public (double X, double Y) Authorize(
        string recognizedText,
        float confidence,
        Rectangle detectedTextBounds,
        ReviewedGroundTruth groundTruth,
        JsonElement review)
    {
        ArgumentNullException.ThrowIfNull(groundTruth);

        if (review.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Review document is invalid or missing.");

        JsonElement decision = RequireObject(review, "Decision");
        JsonElement integrity = RequireObject(review, "Integrity");
        JsonElement geometry = RequireObject(review, "GeometryReview");

        RequireTrue(integrity, "AnnotationDigestVerified");
        RequireTrue(integrity, "ScreenshotDigestVerified");
        RequireTrue(integrity, "ScreenshotDimensionsVerified");
        RequireTrue(geometry, "TextBoundsConfirmed");
        RequireTrue(geometry, "ButtonBoundsConfirmed");
        RequireTrue(geometry, "SafeClickAreaConfirmed");

        if (!decision.TryGetProperty("Status", out JsonElement status)
            || status.ValueKind != JsonValueKind.String
            || !string.Equals(status.GetString(), "Approved", StringComparison.Ordinal))
            throw new InvalidOperationException("Review decision is not Approved.");

        RequireTrue(decision, "GroundTruthApproved");
        RequireTrue(decision, "ClickAuthorized");

        return _geometryAuthorization.Authorize(
            recognizedText, confidence, detectedTextBounds, groundTruth);
    }

    /// <summary>
    /// Reads a required object property without allowing missing or malformed data.
    /// </summary>
    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Review {name} is missing or invalid.");
        return value;
    }

    /// <summary>
    /// Requires an explicitly present JSON boolean true value.
    /// </summary>
    private static void RequireTrue(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException($"Review {name} must be true.");
    }
}
