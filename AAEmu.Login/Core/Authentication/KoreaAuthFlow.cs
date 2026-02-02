using System.Net;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow for Korea supporting password, OTP, certificate, and ARS verification steps.
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
public class KoreaAuthFlow(ILoginController loginController, IOptions<KoreaAuthOptions> options, string username,
    IPAddress clientIp)
    : IChallengeAuthFlow, IOtpAuthFlow, ICertAuthFlow, IArsAuthFlow
{
    private enum State
    {
        AwaitingPassword,
        AwaitingOtp,
        AwaitingCert,
        AwaitingArs,
        Completed
    }

    private readonly KoreaAuthOptions _options = options.Value;

    private State _state = State.AwaitingPassword;
    private AccountId _authenticatedAccountId;

    // Step requirements (hardcoded false for now - no DB/controller support)
    private bool _requiresOtp;
    private bool _requiresCert;
    private bool _requiresArs;

    // Retry tracking
    private int _otpAttempts;
    private int _certAttempts;

    public async Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken)
    {
        // Challenge the client to provide the password.
        await client.SendChallengeAsync(cancellationToken);

        return new AuthFlowResult.Pending();
    }

    public async Task<AuthFlowResult> ContinueAsync(ILoginClient client, string password,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingPassword)
        {
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
        }

        var result = await loginController.Login(username, Password.FromSha256Hex(password), clientIp, cancellationToken);

        if (!result.Success)
        {
            return new AuthFlowResult.Denied(result.DenialReason);
        }

        _authenticatedAccountId = result.AccountId;

        // TODO: Query account requirements from database/controller
        // For now, these are hardcoded to false
        _requiresOtp = false;
        _requiresCert = false;
        _requiresArs = false;

        return await AdvanceToNextStepAsync(client, cancellationToken);
    }

    public async Task<AuthFlowResult> SubmitOtpAsync(ILoginClient client, string otpCode,
        CancellationToken cancellationToken)
    {
        if (_state != State.AwaitingOtp)
        {
            return new AuthFlowResult.Denied(LoginDeniedReason.BadResponse);
        }

        _otpAttempts++;

        // TODO: Actual OTP validation (e.g., TOTP algorithm)
        var isValid = false;

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

        // TODO: Actual certificate validation
        var isValid = false;

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
    /// </summary>
    private async Task<AuthFlowResult> AdvanceToNextStepAsync(ILoginClient client,
        CancellationToken cancellationToken)
    {
        // Check each remaining step in order
        if (_state <= State.AwaitingPassword && _requiresOtp)
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

            // TODO: Generate actual ARS code
            const string ArsCode = "1234";
            await client.SendArsPromptAsync(ArsCode, _options.ArsTimeout, cancellationToken);
            return new AuthFlowResult.Pending();
        }

        // All steps complete
        _state = State.Completed;
        return new AuthFlowResult.Success(_authenticatedAccountId, username);
    }
}
