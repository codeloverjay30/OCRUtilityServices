using System.IO.Abstractions;
using System.Security.Cryptography;
using FluentAssertions;
using OCRUtilityServices.Services;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue48ProductionAuthorizationIntegrationTests
{
    [Fact]
    public void Create_WithoutTrustedKeys_ShouldFailClosed()
    {
        IFileSystem fileSystem = new FileSystem();
        Action act = () => TrustedGroundedClickAuthorizationFactory.Create(
            fileSystem, Array.Empty<TrustedReviewerKey>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Trusted reviewer keys must be provisioned*");
    }

    [Fact]
    public void Create_WithInvalidTrustedKey_ShouldFailClosed()
    {
        IFileSystem fileSystem = new FileSystem();
        var keys = new[] { new TrustedReviewerKey("reviewer", "key", "") };
        Action act = () => TrustedGroundedClickAuthorizationFactory.Create(fileSystem, keys);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Trusted reviewer key configuration is invalid*");
    }

    [Fact]
    public async Task AuthorizeAsync_InvalidApproval_ShouldFailBeforeAnyFileRead()
    {
        var fileSystem = new FileSystem();
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new[]
        {
            new TrustedReviewerKey("reviewer", "key", signer.ExportSubjectPublicKeyInfoPem())
        };
        ITrustedReviewIntegrityAuthorizationService sut =
            TrustedGroundedClickAuthorizationFactory.Create(fileSystem, keys);
        string digest = new('A', 64);
        var approval = new TrustedReviewApprovalEvidence(
            "reviewer", "key", digest, digest, digest,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5),
            "not-base64!");

        Func<Task> act = async () => await sut.AuthorizeAsync(
            approval, "任務", 0.999f,
            CoordinateUtilityServices.Rectangle.FromXYWH(556, 917, 51, 18),
            "missing-review.json", "missing-annotation.json", "missing-screenshot.png",
            digest, digest, digest);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Approval signature is invalid*");
    }

    [Fact]
    public async Task AuthorizeAsync_Cancelled_ShouldFailBeforeAnyFileRead()
    {
        var fileSystem = new FileSystem();
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new[]
        {
            new TrustedReviewerKey("reviewer", "key", signer.ExportSubjectPublicKeyInfoPem())
        };
        ITrustedReviewIntegrityAuthorizationService sut =
            TrustedGroundedClickAuthorizationFactory.Create(fileSystem, keys);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = async () => await sut.AuthorizeAsync(
            null!, "任務", 0.999f,
            CoordinateUtilityServices.Rectangle.FromXYWH(556, 917, 51, 18),
            "missing-review.json", "missing-annotation.json", "missing-screenshot.png",
            new string('A', 64), new string('A', 64), new string('A', 64),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
