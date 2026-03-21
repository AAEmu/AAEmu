#nullable enable

using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Core.Services.TwoFactor;
using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Login.Core.Authentication;

public class KoreaAuthFlowTests
{
    private readonly Mock<ILoginController> _loginController = Mock.Of<ILoginController>();
    private readonly Mock<ITwoFactorService> _twoFactorService = Mock.Of<ITwoFactorService>();
    private readonly Mock<IOtpService> _otpService = Mock.Of<IOtpService>();
    private readonly Mock<IPcCertService> _pcCertService = Mock.Of<IPcCertService>();
    private readonly Mock<IArsService> _arsService = Mock.Of<IArsService>();
    private readonly Mock<ILoginClient> _client = Mock.Of<ILoginClient>();

    public KoreaAuthFlowTests()
    {
        // Default: no 2FA requirements
        _twoFactorService
            .GetRequirementsAsync(Any<AccountId>(), Any<CancellationToken>())
            .Returns(new TwoFactorRequirements(false, false, false));
    }

    private KoreaAuthFlow CreateFlow(string username, KoreaAuthOptions? options = null)
    {
        var opts = Options.Create(options ?? new KoreaAuthOptions() { Enabled = true});
        return new KoreaAuthFlow(
            _loginController.Object,
            _twoFactorService.Object,
            _otpService.Object,
            _pcCertService.Object,
            _arsService.Object,
            opts,
            username,
            IPAddress.Loopback);
    }

    /// <summary>
    /// Creates a <see cref="KoreaAuthInfo"/> with a real sha256_crypt hash for the given password.
    /// Uses fast (1000) rounds to keep unit tests speedy.
    /// </summary>
    private static KoreaAuthInfo MakeKoreaAuthInfo(AccountId accountId, string password,
        string salt = "unitsalttestABCD", int rounds = 1000)
    {
        var rawHash = new byte[32];
        Sha256Crypt.ComputeRaw(password, salt, rounds, rawHash);
        return new KoreaAuthInfo(accountId, rawHash.AsMemory(), salt, rounds);
    }

    /// <summary>
    /// Computes the correct V2 challenge response for <paramref name="hc"/> given the raw AES-256
    /// <paramref name="key"/>. Mirrors the client's computation for testing round-trips.
    /// </summary>
    private static uint[] ComputeExpectedV2Response(ReadOnlySpan<byte> key, uint[] hc)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key.ToArray();
        Span<byte> iv = stackalloc byte[16]; // zero-initialised

        // Step 1: marshal hc uints → bytes
        var hcBytes = new byte[32];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(hcBytes.AsSpan(i * 4, 4), hc[i]);

        // Step 2: t = AES_CBC_decrypt(key, IV=zeros, hc_bytes)
        var t = aes.DecryptCbc(hcBytes, iv, System.Security.Cryptography.PaddingMode.None);

        // Step 3: each uint32 += 1
        for (var i = 0; i < 8; i++)
        {
            var val = BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(i * 4, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(i * 4, 4), unchecked(val + 1));
        }

        // Step 4: ch = AES_CBC_encrypt(key, IV=zeros, t)
        var encrypted = aes.EncryptCbc(t, iv, System.Security.Cryptography.PaddingMode.None);

        var result = new uint[8];
        for (var i = 0; i < 8; i++)
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(encrypted.AsSpan(i * 4, 4));
        return result;
    }

    #region StartAsync Tests

    [Test]
    public async Task StartAsync_WithValidKoreaHash_SendsChallenge2AndReturnsPending()
    {
        // Arrange
        var accountId = new AccountId(42);
        var info = MakeKoreaAuthInfo(accountId, "password");
        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns(info);

        var flow = CreateFlow("testuser");

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Pending>();
        _client.SendChallenge2Async(
            Any<int>(), Any<string>(), Any<uint[]>(), Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task StartAsync_AccountNotFound_ReturnsDenied()
    {
        // Arrange
        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns((KoreaAuthInfo?)null);

        var flow = CreateFlow("unknownuser");

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadAccount);
    }

    [Test]
    public async Task StartAsync_NoKoreaChallengeHash_ReturnsDenied()
    {
        // Arrange — null return = account exists but no korea_challenge_hash stored
        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns((KoreaAuthInfo?)null);

        var flow = CreateFlow("nokoreauser");

        // Act
        var result = await flow.StartAsync(_client.Object, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
    }

    #endregion

    #region Continue2Async (V2 Challenge) Tests

    [Test]
    public async Task Continue2Async_CorrectResponse_ReturnsSuccess()
    {
        // Arrange
        const string Password = "correctpassword";
        var accountId = new AccountId(100);
        var info = MakeKoreaAuthInfo(accountId, Password);

        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns(info);
        _loginController
            .CheckBanStatusAsync(Any<AccountId>(), Any<CancellationToken>())
            .Returns((false, default));

        uint[]? capturedHc = null;
        _client.SendChallenge2Async(Any<int>(), Any<string>(), Any<uint[]>(), Any<CancellationToken>())
            .Callback((_, _, hc, _) => capturedHc = hc);

        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        var correctCh = ComputeExpectedV2Response(info.ChallengeKeyHash.Span, capturedHc!);

        // Act
        var result = await flow.ContinueV2Async(_client.Object, correctCh, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Success>();
        var success = (AuthFlowResult.Success)result;
        await Assert.That(success.AccountId).IsEqualTo(accountId);
        await Assert.That(success.Username).IsEqualTo("testuser");
    }

    [Test]
    public async Task Continue2Async_WrongResponse_ReturnsDenied()
    {
        // Arrange
        var accountId = new AccountId(100);
        var info = MakeKoreaAuthInfo(accountId, "password");

        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns(info);

        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        var wrongCh = new uint[8]; // all zeros — wrong response

        // Act
        var result = await flow.ContinueV2Async(_client.Object, wrongCh, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadAccount);
    }

    [Test]
    public async Task Continue2Async_WhenNotAwaitingChallengeResponse_ReturnsDenied()
    {
        // Arrange — bypass StartAsync by setting state directly
        var flow = CreateFlow("testuser");
        SetFlowState(flow, "Completed");

        // Act
        var result = await flow.ContinueV2Async(_client.Object, new uint[8], CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    [Test]
    public async Task Continue2Async_BannedAccount_ReturnsDenied()
    {
        // Arrange
        const string Password = "password";
        var accountId = new AccountId(200);
        var info = MakeKoreaAuthInfo(accountId, Password);

        _loginController
            .GetKoreaAuthInfoAsync(Any<string>(), Any<CancellationToken>())
            .Returns(info);
        _loginController
            .CheckBanStatusAsync(Any<AccountId>(), Any<CancellationToken>())
            .Returns((true, LoginDeniedReason.BadAccount));

        uint[]? capturedHc = null;
        _client.SendChallenge2Async(Any<int>(), Any<string>(), Any<uint[]>(), Any<CancellationToken>())
            .Callback((int _, string _, uint[] hc, CancellationToken _) => capturedHc = hc);

        var flow = CreateFlow("testuser");
        await flow.StartAsync(_client.Object, CancellationToken.None);

        var correctCh = ComputeExpectedV2Response(info.ChallengeKeyHash.Span, capturedHc!);

        // Act
        var result = await flow.ContinueV2Async(_client.Object, correctCh, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
    }

    #endregion

    #region ContinueAsync (V1 — always denied) Tests

    [Test]
    public async Task ContinueAsync_V1_AlwaysReturnsDenied()
    {
        // Arrange
        var flow = CreateFlow("testuser");

        // Act — V1 ContinueAsync should always deny regardless of state
        var result = await flow.ContinueAsync(_client.Object, new uint[4], new byte[32],
            CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Denied>();
        var denied = (AuthFlowResult.Denied)result;
        await Assert.That(denied.Reason).IsEqualTo(LoginDeniedReason.BadResponse);
    }

    #endregion

    #region SubmitOtpAsync Tests

    [Test]
    public async Task SubmitOtpAsync_WhenNotAwaitingOtp_ReturnsDenied()
    {
        // Arrange — flow is in AwaitingChallengeResponse state (not AwaitingOtp)
        var flow = CreateFlow("testuser");

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
        var options = new KoreaAuthOptions { MaxOtpAttempts = 3 };
        _otpService
            .ValidateAsync(Any<AccountId>(), Any<string>(), Any<CancellationToken>())
            .Returns(new OtpValidationResult(false));
        var flow = CreateFlow("testuser", options);
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
        var options = new KoreaAuthOptions { MaxOtpAttempts = 2 };
        _otpService
            .ValidateAsync(Any<AccountId>(), Any<string>(), Any<CancellationToken>())
            .Returns(new OtpValidationResult(false));
        var flow = CreateFlow("testuser", options);
        SetFlowState(flow, "AwaitingOtp");

        // First attempt
        await flow.SubmitOtpAsync(_client.Object, "000000", CancellationToken.None);

        // Act — second attempt (max reached)
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
        // Arrange — flow is in AwaitingChallengeResponse state
        var flow = CreateFlow("testuser");

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
        var options = new KoreaAuthOptions { MaxCertAttempts = 3 };
        _pcCertService
            .ValidateAsync(Any<AccountId>(), Any<string>(), Any<CancellationToken>())
            .Returns(new PcCertValidationResult(false));
        var flow = CreateFlow("testuser", options);
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
        var options = new KoreaAuthOptions { MaxCertAttempts = 2 };
        _pcCertService
            .ValidateAsync(Any<AccountId>(), Any<string>(), Any<CancellationToken>())
            .Returns(new PcCertValidationResult(false));
        var flow = CreateFlow("testuser", options);
        SetFlowState(flow, "AwaitingCert");

        // First attempt
        await flow.SubmitCertAsync(_client.Object, "00000000", CancellationToken.None);

        // Act — second attempt (max reached)
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
        // Arrange — flow is in AwaitingChallengeResponse state
        var flow = CreateFlow("testuser");

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
        var flow = CreateFlow("testuser");
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
        var accountId = new AccountId(999);
        var flow = CreateFlow("testuser");
        SetFlowState(flow, "AwaitingArs");
        SetAccountId(flow, accountId);
        _loginController
            .CheckBanStatusAsync(Any<AccountId>(), Any<CancellationToken>())
            .Returns((false, default));

        // Act
        var result = await flow.CompleteArsAsync(_client.Object, true, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTypeOf<AuthFlowResult.Success>();
        var success = (AuthFlowResult.Success)result;
        await Assert.That(success.AccountId).IsEqualTo(accountId);
    }

    #endregion

    #region Helper Methods

    private static void SetFlowState(KoreaAuthFlow flow, string stateName)
    {
        var stateField = typeof(KoreaAuthFlow).GetField("_state",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var stateType = typeof(KoreaAuthFlow).GetNestedType("State",
            BindingFlags.NonPublic);

        if (stateField != null && stateType != null)
        {
            var stateValue = Enum.Parse(stateType, stateName);
            stateField.SetValue(flow, stateValue);
        }
    }

    private static void SetAccountId(KoreaAuthFlow flow, AccountId accountId)
    {
        var field = typeof(KoreaAuthFlow).GetField("_accountId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(flow, accountId);
    }

    #endregion
}
