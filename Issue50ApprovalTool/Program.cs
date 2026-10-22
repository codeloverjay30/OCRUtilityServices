using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OCRUtilityServices.Services;

return Issue50ApprovalProgram.Run(args);

/// <summary>
/// Signs and verifies independently reviewed Issue 50 evidence.
/// </summary>
internal static class Issue50ApprovalProgram
{
    private const string ReviewName = "task-home.review.json";
    private const string AnnotationName = "task-home.ground-truth.reviewed.json";
    private const string ScreenshotName = "task-home-original.png";
    private const string ApprovalName = "task-home.approval.json";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Runs the command-line application and returns a process exit code.
    /// </summary>
    public static int Run(string[] args)
    {
        try
        {
            // sign|verify <review-directory> <key-pem> <reviewer-id> <key-id>
            //             <admin-approved-spki-der-sha256>
            if (args.Length == 2 && args[0] == "fingerprint")
            {
                PrintFingerprint(Path.GetFullPath(args[1]));
                return 0;
            }

            if (args.Length != 6 || (args[0] != "sign" && args[0] != "verify"))
            {
                Usage();
                return 2;
            }

            string directory = Path.GetFullPath(args[1]);
            string keyPath = Path.GetFullPath(args[2]);
            string reviewerId = args[3];
            string keyId = args[4];
            string trustedFingerprint = NormalizeDigest(args[5], "Trusted public key fingerprint");

            ArgumentException.ThrowIfNullOrWhiteSpace(reviewerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

            Evidence evidence = ReadEvidence(directory);
            ValidateReview(evidence, reviewerId);

            if (args[0] == "sign")
                Sign(directory, keyPath, reviewerId, keyId, trustedFingerprint, evidence);
            else
                Verify(directory, keyPath, reviewerId, keyId, trustedFingerprint, evidence);

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or CryptographicException
            or JsonException or NotSupportedException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"Approval operation failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Prints the DER SubjectPublicKeyInfo SHA-256 fingerprint of a public PEM key.
    /// </summary>
    private static void PrintFingerprint(string publicKeyPath)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(publicKeyPath));
        RequireP256(key);
        Console.WriteLine($"SPKI-DER-SHA256: {Fingerprint(key)}");
    }

    /// <summary>
    /// Signs approved evidence only when the private key matches the administrator-approved fingerprint.
    /// </summary>
    private static void Sign(string directory, string privateKeyPath,
        string reviewerId, string keyId, string trustedFingerprint, Evidence evidence)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(privateKeyPath));
        RequireP256(key);

        // The trust fingerprint must come from a separately controlled source.
        string derivedPublicPem = key.ExportSubjectPublicKeyInfoPem();
        string actualFingerprint = Fingerprint(key);
        RequireDigestMatch(trustedFingerprint, actualFingerprint, "Trusted public key");

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        var unsigned = new TrustedReviewApprovalEvidence(
            reviewerId, keyId, evidence.ReviewHash, evidence.AnnotationHash,
            evidence.ScreenshotHash, now, now.AddMinutes(10), string.Empty);

        var signer = new TrustedReviewApprovalSigner();
        TrustedReviewApprovalEvidence signed = signer.Sign(unsigned, key);

        // Local signature self-check; trust is enforced by the fingerprint above.
        var verifier = new TrustedReviewApprovalVerifier(new[]
        {
            new TrustedReviewerKey(reviewerId, keyId, derivedPublicPem)
        });
        verifier.Verify(signed, evidence.ReviewHash, evidence.AnnotationHash, evidence.ScreenshotHash);

        // Fail if evidence changed after the initial read.
        EnsureUnchanged(evidence, ReadEvidence(directory));

        string approvalPath = Path.Combine(directory, ApprovalName);
        byte[] output = JsonSerializer.SerializeToUtf8Bytes(signed, JsonOptions);
        try
        {
            using FileStream stream = new(approvalPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            try
            {
                stream.Write(output);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                stream.Dispose();
                File.Delete(approvalPath);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(output);
        }

        Console.WriteLine($"SIGNED APPROVAL CREATED: {approvalPath}");
        Console.WriteLine($"Review SHA-256: {evidence.ReviewHash}");
        Console.WriteLine($"Expires UTC: {signed.ExpiresAtUtc:O}");
    }

    /// <summary>
    /// Verifies signed approval using an independently fingerprint-pinned public key.
    /// </summary>
    private static void Verify(string directory, string publicKeyPath,
        string reviewerId, string keyId, string trustedFingerprint, Evidence evidence)
    {
        byte[] pemBytes = File.ReadAllBytes(publicKeyPath);
        string publicPem = Encoding.UTF8.GetString(pemBytes);
        using ECDsa publicKey = ECDsa.Create();
        publicKey.ImportFromPem(publicPem);
        RequireP256(publicKey);

        RequireDigestMatch(trustedFingerprint, Fingerprint(publicKey), "Trusted public key");

        string approvalPath = Path.Combine(directory, ApprovalName);
        TrustedReviewApprovalEvidence approval =
            JsonSerializer.Deserialize<TrustedReviewApprovalEvidence>(
                File.ReadAllBytes(approvalPath))
            ?? throw new InvalidOperationException("Approval JSON is empty.");

        if (!string.Equals(approval.ReviewerId, reviewerId, StringComparison.Ordinal)
            || !string.Equals(approval.KeyId, keyId, StringComparison.Ordinal))
            throw new InvalidOperationException("Approval reviewer or key identity mismatch.");

        var verifier = new TrustedReviewApprovalVerifier(new[]
        {
            new TrustedReviewerKey(reviewerId, keyId, publicPem)
        });
        verifier.Verify(approval, evidence.ReviewHash, evidence.AnnotationHash, evidence.ScreenshotHash);
        EnsureUnchanged(evidence, ReadEvidence(directory));

        Console.WriteLine("SIGNED APPROVAL VERIFIED");
        Console.WriteLine($"Reviewer: {approval.ReviewerId}");
        Console.WriteLine($"Key ID: {approval.KeyId}");
        Console.WriteLine($"Review SHA-256: {evidence.ReviewHash}");
    }

    /// <summary>
    /// Reads immutable evidence content and calculates its SHA-256 digests.
    /// </summary>
    private static Evidence ReadEvidence(string directory)
    {
        byte[] review = File.ReadAllBytes(Path.Combine(directory, ReviewName));
        byte[] annotation = File.ReadAllBytes(Path.Combine(directory, AnnotationName));
        byte[] screenshot = File.ReadAllBytes(Path.Combine(directory, ScreenshotName));
        using JsonDocument document = JsonDocument.Parse(review);
        return new Evidence(document.RootElement.Clone(),
            Convert.ToHexString(SHA256.HashData(review)),
            Convert.ToHexString(SHA256.HashData(annotation)),
            Convert.ToHexString(SHA256.HashData(screenshot)));
    }

    /// <summary>
    /// Fails closed when the reviewer decision or evidence declarations are incomplete.
    /// </summary>
    private static void ValidateReview(Evidence evidence, string reviewerId)
    {
        JsonElement root = evidence.Review;
        JsonElement reviewer = root.GetProperty("Reviewer");
        JsonElement decision = root.GetProperty("Decision");
        JsonElement integrity = root.GetProperty("Integrity");
        JsonElement geometry = root.GetProperty("GeometryReview");

        if (!string.Equals(reviewer.GetProperty("Id").GetString(),
                reviewerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Reviewer's identity does not match.");

        if (!reviewer.GetProperty("ReviewedAtUtc").TryGetDateTimeOffset(
                out DateTimeOffset reviewedAt) ||
            reviewedAt > TimeProvider.System.GetUtcNow())
            throw new InvalidOperationException("Reviewer decision time is missing or invalid.");

        RequireTrue(decision.GetProperty("Status").GetString() == "Approved",
            "Review decision is not Approved.");
        RequireTrue(decision.GetProperty("GroundTruthApproved").GetBoolean(),
            "Review GroundTruthApproved must be true.");
        RequireTrue(decision.GetProperty("ClickAuthorized").GetBoolean(),
            "Review ClickAuthorized must be true.");

        foreach (string property in new[] {
            "AnnotationDigestVerified", "ScreenshotDigestVerified",
            "ScreenshotDimensionsVerified" })
            RequireTrue(integrity.GetProperty(property).GetBoolean(),
                $"Review {property} must be true.");

        foreach (string property in new[] {
            "TextBoundsConfirmed", "ButtonBoundsConfirmed", "SafeClickAreaConfirmed" })
            RequireTrue(geometry.GetProperty(property).GetBoolean(),
                $"Review {property} must be true.");

        RequireTrue(root.GetProperty("Fixture").GetString() == ScreenshotName,
            "Review fixture mismatch.");
        RequireTrue(root.GetProperty("AnnotationFile").GetString() == AnnotationName,
            "Review annotation filename mismatch.");
        RequireTrue(root.GetProperty("ImageWidth").GetInt32() == 2340 &&
            root.GetProperty("ImageHeight").GetInt32() == 1080,
            "Review image dimensions mismatch.");
        RequireTrue(root.GetProperty("Target").GetProperty("ExpectedText").GetString() == "任務" &&
            root.GetProperty("Target").GetProperty("VisualIdentityConfirmed").GetBoolean(),
            "Review target identity is not confirmed.");

        // Validate optional top-level digests when present.
        if (root.TryGetProperty("AnnotationSha256", out JsonElement annotationDigest))
        {
            RequireDigestMatch(
                evidence.AnnotationHash,
                annotationDigest.GetString(),
                "Top-level annotation");
        }


        if (root.TryGetProperty("ScreenshotSha256", out JsonElement screenshotDigest))
        {
            RequireDigestMatch(
                evidence.ScreenshotHash,
                screenshotDigest.GetString(),
                "Top-level screenshot");
        }



        if (!integrity.TryGetProperty("AnnotationSha256", out JsonElement annotationHash))
        {
            throw new InvalidOperationException(
                "Required Integrity.AnnotationSha256 is missing.");
        }

        if (!integrity.TryGetProperty("ScreenshotSha256", out JsonElement screenshotHash))
        {
            throw new InvalidOperationException(
                "Required Integrity.ScreenshotSha256 is missing.");
        }

        RequireDigestMatch(
            evidence.AnnotationHash,
            annotationHash.GetString(),
            "Integrity annotation");
    

        RequireDigestMatch(
            evidence.ScreenshotHash,
            screenshotHash.GetString(),
            "Integrity screenshot");
    
    }

    /// <summary>
    /// Ensures the key uses the named NIST P-256 elliptic curve.
    /// </summary>
    private static void RequireP256(ECDsa key)
    {
        if (key.KeySize != 256 ||
            key.ExportParameters(false).Curve.Oid.Value != P256Oid)
            throw new InvalidOperationException("ECDSA NIST P-256 key is required.");
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint of DER-encoded SubjectPublicKeyInfo.
    /// </summary>
    private static string Fingerprint(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        byte[] der = key.ExportSubjectPublicKeyInfo();
        return Convert.ToHexString(SHA256.HashData(der));
    }

    /// <summary>
    /// Validates and normalizes a 64-character hexadecimal SHA-256 digest.
    /// </summary>
    private static string NormalizeDigest(string? value, string description)
    {
        if (value is not { Length: 64 })
        {
            throw new ArgumentException(
                $"{description} must contain 64 hexadecimal characters.");
        }

        ReadOnlySpan<char> hex = value.AsSpan();

        foreach (char character in hex)
        {
            bool isHexDigit =
                character is >= '0' and <= '9' or
                             >= 'A' and <= 'F' or
                             >= 'a' and <= 'f';

            if (!isHexDigit)
            {
                throw new ArgumentException(
                    $"{description} must contain 64 hexadecimal characters.");
            }
        }

        return value.ToUpperInvariant();
    }

    /// <summary>
    /// Rejects mismatched SHA-256 digests.
    /// </summary>
    private static void RequireDigestMatch(string expected, string? actual, string description)
    {
        byte[] left = Convert.FromHexString(NormalizeDigest(expected, description));
        byte[] right = Convert.FromHexString(NormalizeDigest(actual, description));
        if (!CryptographicOperations.FixedTimeEquals(left, right))
            throw new InvalidOperationException($"{description} SHA-256 mismatch.");
    }

    /// <summary>
    /// Rejects incomplete reviewer attestations.
    /// </summary>
    private static void RequireTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Rejects evidence modifications between processing stages.
    /// </summary>
    private static void EnsureUnchanged(Evidence original, Evidence current)
    {
        RequireDigestMatch(original.ReviewHash, current.ReviewHash, "Review changed");
        RequireDigestMatch(original.AnnotationHash, current.AnnotationHash, "Annotation changed");
        RequireDigestMatch(original.ScreenshotHash, current.ScreenshotHash, "Screenshot changed");
    }

    /// <summary>
    /// Displays the required command syntax.
    /// </summary>
    private static void Usage() =>
        Console.Error.WriteLine(
            "Usage:\n" +
            "  Issue50ApprovalTool sign <review-dir> <private.pem> <reviewer-id> <key-id> <trusted-spki-der-sha256>\n" +
            "  Issue50ApprovalTool verify <review-dir> <trusted-public.pem> <reviewer-id> <key-id> <trusted-spki-der-sha256>");

    /// <summary>
    /// Stores the parsed review and SHA-256 digests of the three evidence files.
    /// </summary>
    private sealed record Evidence(JsonElement Review, string ReviewHash,
        string AnnotationHash, string ScreenshotHash);
}