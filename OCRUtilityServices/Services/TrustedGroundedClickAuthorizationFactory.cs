using System.IO.Abstractions;

namespace OCRUtilityServices.Services;

/// <summary>
/// Constructs the trusted grounded-click authorization boundary from externally provisioned keys.
/// This factory never performs device input.
/// </summary>
public static class TrustedGroundedClickAuthorizationFactory
{
    /// <summary>
    /// Creates a complete authorization pipeline that verifies signed approval before reviewing evidence and geometry.
    /// </summary>
    public static ITrustedReviewIntegrityAuthorizationService Create(
        IFileSystem fileSystem,
        IEnumerable<TrustedReviewerKey> trustedKeys,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(trustedKeys);

        TrustedReviewerKey[] keys = trustedKeys.ToArray();
        if (keys.Length == 0)
            throw new InvalidOperationException("Trusted reviewer keys must be provisioned.");

        foreach (TrustedReviewerKey key in keys)
        {
            if (key is null ||
                string.IsNullOrWhiteSpace(key.ReviewerId) ||
                string.IsNullOrWhiteSpace(key.KeyId) ||
                string.IsNullOrWhiteSpace(key.PublicKeyPem))
                throw new InvalidOperationException("Trusted reviewer key configuration is invalid.");
        }

        var verifier = new TrustedReviewApprovalVerifier(keys, timeProvider);
        var loader = new GroundTruthLoader(fileSystem);
        var validator = new GroundedClickValidator();
        var geometry = new GroundedClickAuthorizationService(validator);
        var reviewGate = new ReviewAwareGroundedClickAuthorizationService(geometry);
        var integrity = new ReviewIntegrityAuthorizationService(fileSystem, loader, reviewGate);
        return new TrustedReviewIntegrityAuthorizationService(verifier, integrity);
    }
}
