using System.Text.Json;
using CoordinateUtilityServices;
using OCRUtilityServices.Models;

namespace OCRUtilityServices.Services;

/// <summary>
/// Defines the review-aware boundary for OCR grounded-click authorization.
/// </summary>
public interface IReviewAwareGroundedClickAuthorizationService
{
    /// <summary>
    /// Validates review approval and OCR geometry before returning a proposed point.
    /// This method does not perform device input.
    /// </summary>
    (double X, double Y) Authorize(
        string recognizedText,
        float confidence,
        Rectangle detectedTextBounds,
        ReviewedGroundTruth groundTruth,
        JsonElement review);
}