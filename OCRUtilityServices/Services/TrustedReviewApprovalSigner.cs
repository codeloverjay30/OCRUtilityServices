using System.Security.Cryptography;
using OCRUtilityServices.Services;

/// <summary>
/// Signs an independently approved review using an externally managed ECDSA key.
/// </summary>
public sealed class TrustedReviewApprovalSigner
{
    /// <summary>
    /// Signs approval evidence using the exact payload required by the production verifier.
    /// </summary>
    public TrustedReviewApprovalEvidence Sign(
        TrustedReviewApprovalEvidence unsignedApproval,
        ECDsa reviewerPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(unsignedApproval);
        ArgumentNullException.ThrowIfNull(reviewerPrivateKey);

        if (reviewerPrivateKey.KeySize != 256)
        {
            throw new InvalidOperationException(
                "Trusted reviewer signing requires an ECDSA P-256 key.");
        }

        if (unsignedApproval.ExpiresAtUtc <= unsignedApproval.ApprovedAtUtc)
        {
            throw new InvalidOperationException(
                "Approval expiration must be after approval time.");
        }

        byte[] payload =
            TrustedReviewApprovalVerifier.CreateSignedPayload(unsignedApproval);

        byte[] signature = reviewerPrivateKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return unsignedApproval with
        {
            SignatureBase64 = Convert.ToBase64String(signature)
        };
    }
}