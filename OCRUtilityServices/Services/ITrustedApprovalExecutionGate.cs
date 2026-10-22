using OCRUtilityServices.Services;

namespace OCRUtilityServices.Services;

/// <summary>
/// Defines the security boundary for executing a trusted,
/// single-use approval operation.
/// </summary>
public interface ITrustedApprovalExecutionGate
{
    /// <summary>
    /// Verifies an approval, atomically claims its execution right,
    /// and dispatches the authorized operation at most once.
    /// </summary>
    Task ExecuteAsync(
        TrustedReviewApprovalEvidence approval,
        string expectedReviewSha256,
        string expectedAnnotationSha256,
        string expectedScreenshotSha256,
        Func<CancellationToken, Task> authorizedOperation,
        CancellationToken cancellationToken = default);
}