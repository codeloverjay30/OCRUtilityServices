using CoordinateUtilityServices;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

/// <summary>
/// Candidate acceptance specifications for reviewed text boxes with modest padding.
/// These tests intentionally target the current production validator; positive
/// candidate cases are expected to fail until an approved policy is implemented.
/// </summary>
public sealed class GroundedClickGeometryPolicyCandidateTests
{
    private readonly GroundedClickValidator _sut = new(new ReviewedPaddedTextGeometryPolicy());

    [Theory]
    [InlineData(1106, 744, 125, 21, 1168.5, 754.5)]
    [InlineData(1106, 744, 123, 21, 1167.5, 754.5)]
    [InlineData(1106, 744, 121, 21, 1166.5, 754.5)]
    [Trait("Category", "CandidatePolicyRed")]
    public void Validate_ReviewedPaddedTextRegionWithCenteredDetection_ShouldAuthorizeAfterPolicyChange(
        double x, double y, double width, double height,
        double expectedCenterX, double expectedCenterY)
    {
        // Proposed behavior: tolerate the independently reviewed padding,
        // while retaining size, alignment, and safe-button checks.
        // Current production behavior: rejects these cases due to IoU < 0.70.
        var detected = Rectangle.FromXYWH(x, y, width, height);

        (double X, double Y) point = _sut.Validate(
            "登入遊戲",
            0.999275446f,
            detected,
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340,
            1080,
            isReviewed: true);

        point.X.Should().Be(expectedCenterX);
        point.Y.Should().Be(expectedCenterY);
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_TinyCenteredDetection_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲", 0.999f,
            Rectangle.FromXYWH(1165, 753, 5, 3),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not satisfy reviewed padded-text geometry policy*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_DetectionFarFromReviewedText_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲", 0.999f,
            Rectangle.FromXYWH(1350, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not satisfy reviewed padded-text geometry policy*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_OversizedCenteredDetection_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲", 0.999f,
            Rectangle.FromXYWH(1080, 725, 180, 60),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*do not satisfy reviewed padded-text geometry policy*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_UnexpectedText_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "退出遊戲", 0.999f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not match the expected target*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_UnreviewedAnnotation_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲", 0.999f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has not been reviewed*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_TextCenterOutsideButtonInset_ShouldFailClosed()
    {
        var detected = Rectangle.FromXYWH(1106, 744, 123, 21);

        Action act = () => _sut.Validate(
            "登入遊戲", 0.999f,
            detected,
            "登入遊戲",
            detected,
            Rectangle.FromXYWH(1164, 740, 150, 60),
            2340, 1080, isReviewed: true,
            minimumInset: 5);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside the safe button area*");
    }

    [Fact]
    [Trait("Category", "CandidatePolicySafety")]
    public void Validate_LowConfidence_ShouldFailClosed()
    {
        Action act = () => _sut.Validate(
            "登入遊戲", 0.99f,
            Rectangle.FromXYWH(1106, 744, 123, 21),
            "登入遊戲",
            Rectangle.FromXYWH(1095, 735, 150, 40),
            Rectangle.FromXYWH(1020, 716, 300, 80),
            2340, 1080, isReviewed: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*confidence is below the required threshold*");
    }
}