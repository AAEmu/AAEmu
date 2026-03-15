#nullable enable

using AAEmu.Login.Core.Services;
using AAEmu.Login.Core.Services.TwoFactor;
using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;
using Microsoft.Extensions.Logging.Abstractions;

namespace AAEmu.UnitTests.Login.Core.Services.TwoFactor;

public class PcCertServiceTests
{
    private readonly Mock<ITwoFactorRepository> _twoFactorRepo = Mock.Of<ITwoFactorRepository>();
    private readonly Mock<IPasswordService> _passwordService = Mock.Of<IPasswordService>();

    private static readonly AccountId s_testAccountId = new(42);
    private const string TestPin = "12345678";
    private const string TestHash = "stored_hash";

    private PcCertService CreateSut() => new(
        _twoFactorRepo.Object,
        _passwordService.Object,
        NullLogger<PcCertService>.Instance);

    private static TwoFactorConfig MakeConfig(string? certPinHash) =>
        new(s_testAccountId, TwoFactorMethod.PcCert, null, false, certPinHash, null, false, 0, 0);

    [Test]
    public async Task ValidateAsync_NoConfig_ReturnsFalse()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(default(TwoFactorConfig?));

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_ConfigWithNullHash_ReturnsFalse()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(MakeConfig(null));

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_ConfigWithEmptyHash_ReturnsFalse()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(MakeConfig(string.Empty));

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task ValidateAsync_CorrectPin_ReturnsTrue()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(MakeConfig(TestHash));
        _passwordService.VerifyPassword(TestHash, Password.FromPlaintext(TestPin))
            .Returns(PasswordVerificationResult.Success);

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_CorrectPinLegacyFormat_ReturnsTrue()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(MakeConfig(TestHash));
        _passwordService.VerifyPassword(TestHash, Password.FromPlaintext(TestPin))
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsTrue();
    }

    [Test]
    public async Task ValidateAsync_WrongPin_ReturnsFalse()
    {
        _twoFactorRepo.GetConfigAsync(s_testAccountId, Any<CancellationToken>())
            .Returns(MakeConfig(TestHash));
        _passwordService.VerifyPassword(Any<string>(), Any<Password>())
            .Returns(PasswordVerificationResult.Failed);

        var result = await CreateSut().ValidateAsync(s_testAccountId, TestPin, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Success).IsFalse();
    }
}
