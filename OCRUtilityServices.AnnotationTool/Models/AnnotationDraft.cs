namespace OCRUtilityServices.AnnotationTool.Models;

/// <summary>
/// Represents an unverified OCR annotation proposal.
/// </summary>
public sealed record AnnotationDraft(
    string Fixture,
    string TargetText,
    string Status,
    AnnotationBounds TextBounds,
    AnnotationBounds? ButtonBounds);
