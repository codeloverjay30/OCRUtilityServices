namespace OCRUtilityServices.Services;

/// <summary>
/// Coordinates trusted approval verification, one-time reservation, and a single dispatch.
/// This gate never retries failed or ambiguous external device operations.
/// </summary>
public sealed class TrustedApprovalExecutionGate : ITrustedApprovalExecutionGate
{
    private readonly ITrustedReviewApprovalVerifier _verifier;
    private readonly IApprovalConsumptionStore _store;

    /// <summary>
    /// Initializes the fail-closed execution boundary.
    /// </summary>
    public TrustedApprovalExecutionGate(
        ITrustedReviewApprovalVerifier verifier,
        IApprovalConsumptionStore store)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        TrustedReviewApprovalEvidence approval,
        string reviewSha256,
        string annotationSha256,
        string screenshotSha256,
        Func<CancellationToken, Task> authorizedClick,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(authorizedClick);
        cancellationToken.ThrowIfCancellationRequested();

        _verifier.Verify(approval, reviewSha256, annotationSha256, screenshotSha256);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_store.TryClaim(approval))
            throw new InvalidOperationException("Approval has already been claimed.");

        // No retry and no release on cancellation/timeout: the effect may have happened.
        await authorizedClick(cancellationToken).ConfigureAwait(false);
    }
}
