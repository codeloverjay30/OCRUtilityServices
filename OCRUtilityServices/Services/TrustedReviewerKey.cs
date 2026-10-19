namespace OCRUtilityServices.Services;

/// <summary>
/// Represents an administrator-provisioned reviewer public key and authorization state.
/// </summary>
public sealed record TrustedReviewerKey(
    string ReviewerId,
    string KeyId,
    string PublicKeyPem,
    bool IsRevoked = false);
