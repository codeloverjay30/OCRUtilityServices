using CoordinateUtilityServices;
using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Applies reviewed ground truth to the existing geometry validator.
/// The caller must first establish a unique OCR match.
/// </summary>
public sealed class GroundedClickAuthorizationService : IGroundedClickAuthorizationService
{
    private readonly IGroundedClickValidator _validator;

    /// <summary>
    /// Initializes the authorization service.
    /// </summary>
    public GroundedClickAuthorizationService(IGroundedClickValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc/>
    public (double X, double Y) Authorize(
        string recognizedText, float confidence,
        Rectangle detectedTextBounds, ReviewedGroundTruth groundTruth)
    {
        ArgumentNullException.ThrowIfNull(groundTruth);
        return _validator.Validate(recognizedText, confidence,
            detectedTextBounds, groundTruth.TargetText,
            groundTruth.TextBounds, groundTruth.ButtonBounds,
            groundTruth.ImageWidth, groundTruth.ImageHeight,
            isReviewed: true);
    }
}
