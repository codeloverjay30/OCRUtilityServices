namespace OCRUtilityServices.Services;

/// <summary>
/// Represents an externally signed approval bound to three evidence digests.
/// </summary>
public sealed record TrustedReviewApprovalEvidence(
    string ReviewerId,
    string KeyId,
    string ReviewSha256,
    string AnnotationSha256,
    string ScreenshotSha256,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SignatureBase64);
