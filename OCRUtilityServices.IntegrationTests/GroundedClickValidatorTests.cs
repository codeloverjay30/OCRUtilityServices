using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class GroundedClickValidatorTests
{
    private readonly GroundedClickValidator _sut = new();

    [Fact]
    public void Validate_ReviewedGroundTruth_ShouldReturnTextCenter()
    {
        var detected = Rectangle.FromXYWH(1106, 744, 123, 21);
        var truth = Rectangle.FromXYWH(1106, 744, 123, 21);

        // Synthetic geometry for validator testing only.
        // This is NOT a reviewed annotation of a real screenshot.
        var button = Rectangle.FromXYWH(1080, 720, 180, 70);

        var result = _sut.Validate(
            "登入遊戲",
            0.999f,
            detected,
            "登入遊戲",
            truth,
            button,
            2340,
            1080,
            isReviewed: true);

        result.X.Should().Be(1167.5);
        result.Y.Should().Be(754.5);
    }

    [Fact]
    public void Validate_UnreviewedGroundTruth_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1106, 744, 123, 21),
            Rectangle.FromXYWH(1080, 720, 180, 70),
            2340,
            1080,
            isReviewed: false);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*has not been reviewed*");
    }

    [Fact]
    public void Validate_InsufficientOverlap_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(900, 744, 123, 21),
            Rectangle.FromXYWH(1080, 720, 180, 70),
            2340,
            1080,
            isReviewed: true);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*do not sufficiently overlap*");
    }

    [Fact]
    public void Validate_TextCenterOutsideButton_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1106, 744, 123, 21),
            Rectangle.FromXYWH(1400, 720, 180, 70),
            2340,
            1080,
            isReviewed: true);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*outside the safe button area*");
    }

    [Fact]
    public void Validate_LowConfidence_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.941645205f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1106, 744, 123, 21),
            Rectangle.FromXYWH(1080, 720, 180, 70),
            2340,
            1080,
            isReviewed: true);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*confidence is below*");
    }


    [Fact]
    public void Validate_ReviewedPaddedTextBounds_WithLowIoU_ShouldFailClosedUnderCurrentPolicy()
    {
        // Reproduces the observed geometry, without claiming the OCR result is incorrect.
        // IoU = (123 * 21) / (150 * 40) = 0.4305.
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999275446f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340,
            1080,
            isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not sufficiently overlap ground truth*");
    }

    [Fact]
    public void Validate_TinyDetectionInsideReviewedTextBounds_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            Rectangle.FromXYWH(1165, 753, 5, 3),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340,
            1080,
            isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not sufficiently overlap ground truth*");
    }

    [Fact]
    public void Validate_DetectionOffsetFromReviewedTextBounds_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            Rectangle.FromXYWH(1350, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340,
            1080,
            isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not sufficiently overlap ground truth*");
    }

    [Fact]
    public void Validate_TextCenterWithinTextBoundsButOutsideSafeButtonInset_ShouldFailClosed()
    {
        // The OCR and reviewed text rectangles match exactly, isolating the
        // button-inset rule from the IoU rule.
        var text = Rectangle.FromXYWH(1106, 744, 123, 21);
        var button = Rectangle.FromXYWH(1164, 740, 150, 60);

        Action act = () => _sut.Validate(
            "登入遊戲",
            0.999f,
            text,
            "登入遊戲",
            text,
            button,
            2340,
            1080,
            isReviewed: true,
            minimumInset: 5);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside the safe button area*");
    }

    [Fact]
    public void Validate_ExactTextGeometryAndSafeButton_ShouldReturnDetectedTextCenter()
    {
        var text = Rectangle.FromXYWH(1106, 744, 123, 21);

        (double X, double Y) result = _sut.Validate(
            "登入遊戲",
            0.999f,
            text,
            "登入遊戲",
            text,
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340,
            1080,
            isReviewed: true);

        result.X.Should().Be(1167.5);
        result.Y.Should().Be(754.5);
    }

    [Fact]
    public void Validate_DetectionCenterExactlyOnSafeInsetBoundary_ShouldAuthorize()
    {
        var text = Rectangle.FromXYWH(1106, 744, 123, 21);
        // Left edge plus five pixels is exactly the detected center X.
        var button = Rectangle.FromXYWH(1162.5, 720, 150, 70);

        (double X, double Y) result = _sut.Validate(
            "登入遊戲",
            0.999f,
            text,
            "登入遊戲",
            text,
            button,
            2340,
            1080,
            isReviewed: true,
            minimumInset: 5);

        result.X.Should().Be(1167.5);
        result.Y.Should().Be(754.5);
    }
}
