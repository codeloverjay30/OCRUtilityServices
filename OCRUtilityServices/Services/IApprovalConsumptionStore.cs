namespace OCRUtilityServices.Services;

/// <summary>
/// Reserves a signed approval for a single irreversible execution attempt.
/// </summary>
public interface IApprovalConsumptionStore
{
    /// <summary>
    /// Atomically reserves the signed authorization payload; returns false if already reserved.
    /// </summary>
    bool TryClaim(TrustedReviewApprovalEvidence approval);
}
