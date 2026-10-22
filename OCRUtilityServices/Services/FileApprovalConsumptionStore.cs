using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace OCRUtilityServices.Services;

/// <summary>
/// Reserves a signed approval once on a trusted local filesystem.
/// Never deletes claims or automatically retries ambiguous operations.
/// </summary>
public sealed class FileApprovalConsumptionStore : IApprovalConsumptionStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _directory;

    /// <summary>
    /// Creates a store in an existing, independently access-controlled directory.
    /// </summary>
    public FileApprovalConsumptionStore(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = _fileSystem.Path.GetFullPath(directory);
        if (!_fileSystem.Directory.Exists(_directory))
            throw new DirectoryNotFoundException("Approval claim directory does not exist.");
    }

    /// <summary>
    /// Claims the signed payload atomically, independent of randomized ECDSA signature bytes.
    /// </summary>
    public bool TryClaim(TrustedReviewApprovalEvidence approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (string.IsNullOrWhiteSpace(approval.SignatureBase64))
            throw new ArgumentException("Approval signature is missing.", nameof(approval));

        // Verification MUST be performed by the caller immediately before claiming.
        byte[] payload = TrustedReviewApprovalVerifier.CreateSignedPayload(approval);
        try
        {
            string digest = Convert.ToHexString(SHA256.HashData(payload));
            string path = _fileSystem.Path.Combine(_directory, digest + ".claimed");
            try
            {
                using Stream stream = _fileSystem.File.Open(
                    path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] record = Encoding.UTF8.GetBytes(
                    $"ClaimedAtUtc={DateTimeOffset.UtcNow:O}\nPayloadSha256={digest}\n");
                try
                {
                    stream.Write(record);
                    if (stream is FileStream fileStream)
                        fileStream.Flush(flushToDisk: true);
                    else
                        stream.Flush();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(record);
                }
                return true;
            }
            catch (IOException) when (_fileSystem.File.Exists(path))
            {
                // A partially written record is still a consumed/ambiguous claim.
                return false;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
