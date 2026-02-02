#nullable enable

using System.Net;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Login.Core.Authentication;

public class KoreaAuthFlowTests
{
    private readonly Mock<ILoginController> _loginController = Mock.Of<ILoginController>();
    private readonly Mock<ILoginClient> _client = Mock.Of<ILoginClient>();

    private KoreaAuthFlow CreateFlow(string username, KoreaAuthOptions? options = null)
    {
        var opts = Options.Create(options ?? new KoreaAuthOptions());
        return new KoreaAuthFlow(_loginController.Object, opts, username, IPAddress.Loopback);
    }

    #region StartAsync Tests

    [Test]
    public async Task StartAsync_SendsChallengePacket_ReturnsPending()
    {
        // Arrange
        var flow = CreateFlow("testuser");

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Pending>();
        _client.SendChallengeAsync(Any<CancellationToken>()).WasCalled(Times.Once);
    }

    #endregion

    #region ContinueAsync (Password) Tests

    [Test]
    public async Task ContinueAsync_SuccessfulLogin_ReturnsSuccess()
    {
        // Arrange
        const string Username = "testuser";
        const string password = "password123";
        var accountId = new AccountId(456);

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username);
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.ContinueAsync(_client.Object, password, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Success>();
        var success = (AuthFlowResult.Success)result;
        await Assert.That(success.AccountId).IsEqualTo(accountId);
        await Assert.That(success.Username).IsEqualTo(Username);
    }

    [Test]
    public async Task ContinueAsync_UserNotFound_ReturnsDenied()
    {
        // Arrange
        const string Username = "unknownuser";
        const string password = "password123";
        const LoginDeniedReason DenialReason = LoginDeniedReason.BadAccount;

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(false, default, DenialReason));

        var flow = CreateFlow(Username);
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.ContinueAsync(_client.Object, password, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(DenialReason);
    }

    [Test]
    public async Task ContinueAsync_WrongPassword_ReturnsDenied()
    {
        // Arrange
        const string Username = "testuser";
        const string WrongPassword = "wrongpassword";
        const LoginDeniedReason DenialReason = LoginDeniedReason.BadAccount;

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(false, default, DenialReason));

        var flow = CreateFlow(Username);
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.ContinueAsync(_client.Object, WrongPassword, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(DenialReason);
    }

    #endregion

    #region SubmitOtpAsync Tests

    [Test]
    public async Task SubmitOtpAsync_WhenNotAwaitingOtp_ReturnsDenied()
    {
        // Arrange - flow is still in AwaitingPassword state
        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.SubmitOtpAsync(_client.Object, "123456", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    [Test]
    public async Task SubmitOtpAsync_InvalidOtp_SendsRetryPacket_ReturnsPending()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);
        var options = new KoreaAuthOptions { MaxOtpAttempts = 3 };

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username, options);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up OTP state after password authentication
        SetFlowState(flow, "AwaitingOtp");

        // Act
        var result = await flow.SubmitOtpAsync(_client.Object, "000000", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Pending>();
        _client.SendOtpPromptAsync(3, 2, Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task SubmitOtpAsync_MaxAttemptsExceeded_ReturnsDenied()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);
        var options = new KoreaAuthOptions { MaxOtpAttempts = 2 };

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username, options);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up OTP state after password authentication
        SetFlowState(flow, "AwaitingOtp");

        // First attempt
        await flow.SubmitOtpAsync(_client.Object, "000000", CancellationToken.None);

        // Act - second attempt (max reached)
        var result = await flow.SubmitOtpAsync(_client.Object, "000000", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    #endregion

    #region SubmitCertAsync Tests

    [Test]
    public async Task SubmitCertAsync_WhenNotAwaitingCert_ReturnsDenied()
    {
        // Arrange - flow is still in AwaitingPassword state
        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.SubmitCertAsync(_client.Object, "12345678", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    [Test]
    public async Task SubmitCertAsync_InvalidCert_SendsRetryPacket_ReturnsPending()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);
        var options = new KoreaAuthOptions { MaxCertAttempts = 3 };

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username, options);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up Cert state after password authentication
        SetFlowState(flow, "AwaitingCert");

        // Act
        var result = await flow.SubmitCertAsync(_client.Object, "00000000", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Pending>();
        _client.SendCertPromptAsync(3, 2, Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task SubmitCertAsync_MaxAttemptsExceeded_ReturnsDenied()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);
        var options = new KoreaAuthOptions { MaxCertAttempts = 2 };

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username, options);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up Cert state after password authentication
        SetFlowState(flow, "AwaitingCert");

        // First attempt
        await flow.SubmitCertAsync(_client.Object, "00000000", CancellationToken.None);

        // Act - second attempt (max reached)
        var result = await flow.SubmitCertAsync(_client.Object, "00000000", CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    #endregion

    #region CompleteArsAsync Tests

    [Test]
    public async Task CompleteArsAsync_WhenNotAwaitingArs_ReturnsDenied()
    {
        // Arrange - flow is still in AwaitingPassword state
        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        // Act
        var result = await flow.CompleteArsAsync(_client.Object, true, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    [Test]
    public async Task CompleteArsAsync_Failure_ReturnsDenied()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up ARS state after password authentication
        SetFlowState(flow, "AwaitingArs");

        // Act
        var result = await flow.CompleteArsAsync(_client.Object, false, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    [Test]
    public async Task CompleteArsAsync_Success_ReturnsSuccess()
    {
        // Arrange
        const string Username = "testuser";
        var accountId = new AccountId(456);

        _loginController
            .Login(Any<string>(), Any<Password>(), Any<IPAddress>(), Any<CancellationToken>())
            .Returns(new LoginResult(true, accountId, 0));

        var flow = CreateFlow(Username);
        await flow.StartAsync(_client.Object, CancellationToken.None);
        await flow.ContinueAsync(_client.Object, "password", CancellationToken.None);

        // Set up ARS state after password authentication
        SetFlowState(flow, "AwaitingArs");

        // Act
        var result = await flow.CompleteArsAsync(_client.Object, true, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Success>();
        var success = (AuthFlowResult.Success)result;
        await Assert.That(success.AccountId).IsEqualTo(accountId);
        await Assert.That(success.Username).IsEqualTo(Username);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Sets the internal state of the flow using reflection.
    /// </summary>
    /// <param name="flow">The flow to modify.</param>
    /// <param name="stateName">The state name (e.g., "AwaitingOtp", "AwaitingCert", "AwaitingArs").</param>
    private static void SetFlowState(KoreaAuthFlow flow, string stateName)
    {
        var stateField = typeof(KoreaAuthFlow).GetField("_state",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Get the State enum type (nested private enum)
        var stateType = typeof(KoreaAuthFlow).GetNestedType("State",
            System.Reflection.BindingFlags.NonPublic);

        if (stateField != null && stateType != null)
        {
            var stateValue = Enum.Parse(stateType, stateName);
            stateField.SetValue(flow, stateValue);
        }
    }

    #endregion
}
