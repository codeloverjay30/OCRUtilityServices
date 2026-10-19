namespace OCRUtilityServices.Services;

/// <summary>
/// Defines the trusted cryptographic approval boundary.
/// </summary>
public interface ITrustedReviewApprovalVerifier
{
    /// <summary>
    /// Verifies a signed approval against the exact evidence digests.
    /// </summary>
    void Verify(
        TrustedReviewApprovalEvidence evidence,
        string reviewSha256,
        string annotationSha256,
        string screenshotSha256);
}
