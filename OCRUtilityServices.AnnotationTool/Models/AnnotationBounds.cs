namespace OCRUtilityServices.AnnotationTool.Models;

/// <summary>
/// Represents a rectangle in source-image pixel coordinates.
/// </summary>
public sealed record AnnotationBounds(
    double X,
    double Y,
    double Width,
    double Height);
