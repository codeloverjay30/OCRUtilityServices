using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace OCRUtilityServices.Services;

/// <summary>
/// Verifies signed approvals against administrator-provisioned ECDSA P-256 public keys.
/// Trust configuration must come from outside the review and approval documents.
/// </summary>
public sealed class TrustedReviewApprovalVerifier : ITrustedReviewApprovalVerifier
{
    private readonly IReadOnlyDictionary<string, TrustedReviewerKey> _keys;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a verifier with an externally supplied trust list.
    /// </summary>
    public TrustedReviewApprovalVerifier(
        IEnumerable<TrustedReviewerKey> trustedKeys,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);
        _keys = trustedKeys.ToDictionary(k => k.KeyId, StringComparer.Ordinal);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public void Verify(
        TrustedReviewApprovalEvidence evidence,
        string reviewSha256,
        string annotationSha256,
        string screenshotSha256)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        byte[] review = ParseDigest(reviewSha256);
        byte[] annotation = ParseDigest(annotationSha256);
        byte[] screenshot = ParseDigest(screenshotSha256);
        if (!Matches(evidence.ReviewSha256, review) ||
            !Matches(evidence.AnnotationSha256, annotation) ||
            !Matches(evidence.ScreenshotSha256, screenshot))
            throw new InvalidOperationException("Approval evidence digests do not match trusted evidence.");

        if (!_keys.TryGetValue(evidence.KeyId, out TrustedReviewerKey? key) ||
            key.IsRevoked ||
            !string.Equals(key.ReviewerId, evidence.ReviewerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Approval signer is not trusted or has been revoked.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (evidence.ApprovedAtUtc > now ||
            evidence.ExpiresAtUtc <= now ||
            evidence.ExpiresAtUtc <= evidence.ApprovedAtUtc)
            throw new InvalidOperationException("Approval is expired or outside its validity period.");

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(evidence.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Approval signature is invalid.", ex);
        }

        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportFromPem(key.PublicKeyPem);
            if (verifier.KeySize != 256 ||
                !verifier.VerifyData(CreateSignedPayload(evidence), signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidOperationException("Approval signature is invalid.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Approval public key or signature is invalid.", ex);
        }
    }

    /// <summary>
    /// Produces a deterministic, versioned, length-prefixed signature payload.
    /// </summary>
    public static byte[] CreateSignedPayload(TrustedReviewApprovalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("OmniAppium.Issue48.Approval.v1");
            writer.Write(evidence.ReviewerId);
            writer.Write(evidence.KeyId);
            writer.Write(Convert.ToHexString(ParseDigest(evidence.ReviewSha256)));
            writer.Write(Convert.ToHexString(ParseDigest(evidence.AnnotationSha256)));
            writer.Write(Convert.ToHexString(ParseDigest(evidence.ScreenshotSha256)));
            writer.Write(evidence.ApprovedAtUtc.ToUniversalTime().Ticks);
            writer.Write(evidence.ExpiresAtUtc.ToUniversalTime().Ticks);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Parses exactly one SHA-256 digest without permitting ambiguous encodings.
    /// </summary>
    private static byte[] ParseDigest(string? value)
    {
        if (value is null || value.Length != 64)
        {
            throw new InvalidOperationException(
                "Approval SHA-256 digest must contain 64 hexadecimal characters.");
        }

        Span<byte> result = stackalloc byte[32];

        OperationStatus status = Convert.FromHexString(
            value.AsSpan(),
            result,
            out int charsConsumed,
            out int bytesWritten);

        if (status != OperationStatus.Done ||
            charsConsumed != 64 ||
            bytesWritten != 32)
        {
            throw new InvalidOperationException(
                "Approval SHA-256 digest must contain 64 hexadecimal characters.");
        }

        return result.ToArray();
    }

    /// <summary>
    /// Compares decoded digests using constant-time equality.
    /// </summary>
    private static bool Matches(
        string? actual,
        ReadOnlySpan<byte> expected)
    {
        if (actual is null || actual.Length != 64)
        {
            return false;
        }

        Span<byte> decoded = stackalloc byte[32];

        OperationStatus status = Convert.FromHexString(
            actual.AsSpan(),
            decoded,
            out int charsConsumed,
            out int bytesWritten);

        return status == OperationStatus.Done &&
               charsConsumed == 64 &&
               bytesWritten == 32 &&
               CryptographicOperations.FixedTimeEquals(decoded, expected);
    }
}
