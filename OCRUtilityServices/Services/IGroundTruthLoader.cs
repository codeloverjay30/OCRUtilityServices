using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Loads an approved annotation and verifies its exact screenshot binding.
/// </summary>
public interface IGroundTruthLoader
{
    /// <summary>
    /// Loads approved ground truth using out-of-band trusted SHA-256 digests.
    /// </summary>
    Task<ReviewedGroundTruth> LoadAsync(
        string annotationPath,
        string screenshotPath,
        string expectedAnnotationSha256,
        string expectedScreenshotSha256,
        CancellationToken cancellationToken = default);
}
