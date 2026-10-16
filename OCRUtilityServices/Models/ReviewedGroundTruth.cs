using CoordinateUtilityServices;

namespace OCRUtilityServices.Models;

/// <summary>
/// Holds verified annotation geometry bound to an exact screenshot.
/// Instances are created only by the approved annotation loader.
/// </summary>
public sealed class ReviewedGroundTruth
{
    internal ReviewedGroundTruth(string fixture, string targetText,
        Rectangle textBounds, Rectangle buttonBounds, int imageWidth, int imageHeight)
    {
        Fixture = fixture;
        TargetText = targetText;
        TextBounds = textBounds;
        ButtonBounds = buttonBounds;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
    }

    /// <summary>Gets the screenshot file name.</summary>
    public string Fixture { get; }
    /// <summary>Gets the exact target text.</summary>
    public string TargetText { get; }
    /// <summary>Gets the reviewed text rectangle.</summary>
    public Rectangle TextBounds { get; }
    /// <summary>Gets the reviewed button rectangle.</summary>
    public Rectangle ButtonBounds { get; }
    /// <summary>Gets the screenshot width.</summary>
    public int ImageWidth { get; }
    /// <summary>Gets the screenshot height.</summary>
    public int ImageHeight { get; }
}
