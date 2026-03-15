#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;

namespace AAEmu.UnitTests.Login.Core.Authentication;

public class PasswordAuthFlowTests
{
    private readonly Mock<ILoginController> _loginController = Mock.Of<ILoginController>();
    private readonly Mock<ILoginClient> _client = Mock.Of<ILoginClient>();

    [Test]
    public async Task StartAsync_SuccessfulLogin_ReturnsSuccessWithUsername()
    {
        // Arrange
        const string Username = "testuser";
        const string TestPassword = "testpass";
        var accountId = new AccountId(123);

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = new PasswordAuthFlow(_loginController.Object, Username, Password.FromSha256Hex(TestPassword),
            IPAddress.Loopback);

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Success>();
        var success = (AuthFlowResult.Success)result;
        await Assert.That(success.AccountId).IsEqualTo(accountId);
        await Assert.That(success.Username).IsEqualTo(Username);
    }

    [Test]
    public async Task StartAsync_FailedLogin_ReturnsDenied()
    {
        // Arrange
        const string Username = "testuser";
        const string password = "wrongpass";
        const LoginDeniedReason DenialReason = LoginDeniedReason.BadResponse;

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(false, default, DenialReason));

        var flow = new PasswordAuthFlow(_loginController.Object, Username, Password.FromSha256Hex(password), IPAddress.Loopback);

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(DenialReason);
    }

    [Test]
    public async Task StartAsync_BannedAccount_ReturnsDeniedWithBanReason()
    {
        // Arrange
        const string Username = "banneduser";
        const string password = "password";
        const LoginDeniedReason BanReason = LoginDeniedReason.TryTradeCashTemporal;

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(false, default, BanReason));

        var flow = new PasswordAuthFlow(_loginController.Object, Username, Password.FromSha256Hex(password), IPAddress.Loopback);

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(BanReason);
    }
}
