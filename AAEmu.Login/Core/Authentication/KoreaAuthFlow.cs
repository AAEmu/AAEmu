using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Core.Services.TwoFactor;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow for Korea supporting V2 challenge-response, OTP, certificate, and ARS verification steps.
/// </summary>
/// <remarks>
/// This flow implements a state machine that can handle multiple sequential verification steps:
/// <list type="number">
///   <item>Password authentication (always required)</item>
///   <item>OTP verification (if account requires it)</item>
///   <item>Certificate verification (if account requires it)</item>
///   <item>ARS phone verification (if account requires it)</item>
/// </list>
/// Each step is optional based on account configuration. The flow advances through each required step
/// in sequence until all verifications complete.
/// </remarks>
public class KoreaAuthFlow(
    ILoginController loginController,
    ITwoFactorService twoFactorService,
    IOtpService otpService,
    IPcCertService pcCertService,
    IArsService arsService,
    IOptions<KoreaAuthOptions> options,
    string username,
#pragma warning disable CS9113 // Parameter is unread.
    IPAddress clientIp)
#pragma warning restore CS9113 // Parameter is unread.
    : IChallengeAuthFlow, IChallenge2AuthFlow, IOtpAuthFlow, ICertAuthFlow, IArsAuthFlow
{
    private enum State
    {
        AwaitingChallengeResponse,
        AwaitingOtp,
        AwaitingCert,
        AwaitingArs,
        Completed
    }

    private readonly KoreaAuthOptions _options = options.Value;

    private State _state = State.AwaitingChallengeResponse;
    private AccountId _accountId;

    // V2 challenge state (cleared after successful verification)
    private uint[] _v2Hc = [];
    private ReadOnlyMemory<byte> _challengeKey;

    private bool _requiresOtp;
    private bool _requiresCert;
    private bool _requiresArs;

    // Retry tracking
    private int _otpAttempts;
    private int _certAttempts;

    public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return new AuthFlowResult.Denied(LoginDeniedReason.LoginUnknown);

        // Look up the account's Korea challenge material.
        // No ban check here — doing so before authentication would leak whether an account exists.
        var info = await loginController.GetKoreaAuthInfoAsync(username, cancellationToken);
        if (info is null)
        {
            // Account doesn't exist, or has no korea_challenge_hash yet.
            // User must log in via EU auth first (with KoreaChallengeAuth:Enabled=true) to seed the hash.
            return new AuthFlowResult.Denied(LoginDeniedReason.BadAccount);
        }

        _accountId = info.AccountId;
        _challengeKey = info.ChallengeKeyHash;

        // Generate 8 cryptographically secure random uint32 challenge values
        _v2Hc = new uint[8];
        Span<byte> rngBytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(rngBytes);
        for (var i = 0; i < 8; i++)
            _v2Hc[i] = BinaryPrimitives.ReadUInt32LittleEndian(rngBytes.Slice(i * 4, 4));

        await client.SendChallenge2Async(info.ChallengeRounds, info.ChallengeSalt, _v2Hc, cancellationToken);
        return new AuthFlowResult.Pending();
    }

    /// <summary>
    /// V1 challenge-response continuation — always denied.
    /// The server never issues <c>ACChallengePacket</c>; this path should never be reached in production.
    /// </summary>
    public Task<AuthFlowResult> ContinueAsync(ILoginClient client, uint[] ch, byte[] pw,
        CancellationToken cancellationToken)
    {
        // V1 Korea challenge is disabled (SHA-1 without per-account salt is ~17M× weaker than PBKDF2@600k).
        return Task.FromResult<AuthFlowResult>(new AuthFlowResult.Denied(LoginDeniedReason.BadResponse));
    }

    /// <summary>
    /// V2 challenge-response continuation — verifies the AES-256 challenge response.
    /// </summary>
    public async Task<AuthFlowResult> ContinueV2Async(ILoginClient client, uint[] ch,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingChallengeResponse)
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);

        if (!KoreanChallengeVerifier.VerifyV2(_challengeKey.Span, _v2Hc, ch))
            return new AuthFlowResult.Denied(LoginDeniedReason.BadAccount);

        // Clear key material now that verification is done
        _challengeKey = ReadOnlyMemory<byte>.Empty;
        _v2Hc = [];

        // Query 2FA requirements from the database
        var requirements = await twoFactorService.GetRequirementsAsync(_accountId, cancellationToken);
        _requiresOtp = requirements.RequiresOtp;
        _requiresCert = requirements.RequiresPcCert;
        _requiresArs = requirements.RequiresArs;

        return await AdvanceToNextStepAsync(client, cancellationToken);
    }

    public async Task<AuthFlowResult> SubmitOtpAsync(ILoginClient client, string otpCode,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingOtp)
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);

        _otpAttempts++;

        var result = await otpService.ValidateAsync(_accountId, otpCode, cancellationToken);
        var isValid = result.Success;

        if (!isValid)
        {
            if (_otpAttempts >= _options.MaxOtpAttempts)
            {
                return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
            }

            // Send retry packet
            await client.SendOtpPromptAsync(_options.MaxOtpAttempts, _otpAttempts + 1, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        return await AdvanceToNextStepAsync(client, cancellationToken);
    }

    public async Task<AuthFlowResult> SubmitCertAsync(ILoginClient client, string certNumber,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingCert)
        {
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
        }

        _certAttempts++;

        var result = await pcCertService.ValidateAsync(_accountId, certNumber, cancellationToken);
        var isValid = result.Success;

        if (!isValid)
        {
            if (_certAttempts >= _options.MaxCertAttempts)
            {
                return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
            }

            // Send retry packet
            await client.SendCertPromptAsync(_options.MaxCertAttempts, _certAttempts + 1, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        return await AdvanceToNextStepAsync(client, cancellationToken);
    }

    public async Task<AuthFlowResult> CompleteArsAsync(ILoginClient client, bool success,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingArs)
        {
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
        }

        if (!success)
        {
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
        }

        return await AdvanceToNextStepAsync(client, cancellationToken);
    }

    /// <summary>
    /// Advances to the next required verification step, or completes authentication if all steps are done.
    /// Performs the ban check immediately before issuing the final <see cref="AuthFlowResult.Success"/>
    /// to avoid leaking account status prior to successful authentication.
    /// </summary>
    private async Task<AuthFlowResult> AdvanceToNextStepAsync(ILoginClient client,
        CancellationToken cancellationToken)
    {
        // Check each remaining step in order
        if (_state <= State.AwaitingChallengeResponse && _requiresOtp)
        {
            _state = State.AwaitingOtp;
            await client.SendOtpPromptAsync(0, 0, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        if (_state <= State.AwaitingOtp && _requiresCert)
        {
            _state = State.AwaitingCert;
            await client.SendCertPromptAsync(0, 0, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        if (_state <= State.AwaitingCert && _requiresArs)
        {
            _state = State.AwaitingArs;
            var arsTimeoutSeconds = (int)_options.ArsTimeout.TotalSeconds;
            var arsSession = await arsService.CreateSessionAsync(_accountId, arsTimeoutSeconds, cancellationToken);
            await client.SendArsPromptAsync(arsSession.SessionCode, _options.ArsTimeout, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        // All steps complete. Check ban status before issuing success
        _state = State.Completed;
        var (banned, banReason) = await loginController.CheckBanStatusAsync(_accountId, cancellationToken);
        if (banned)
            return new AuthFlowResult.Denied(banReason);

        return new AuthFlowResult.Success(_accountId, username);
    }
}
