using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// The result of an authentication flow step.
/// </summary>
public abstract record AuthFlowResult
{
    private AuthFlowResult() { }

    /// <summary>
    /// Authentication completed successfully.
    /// </summary>
    /// <param name="AccountId">The authenticated account ID.</param>
    /// <param name="Username">The authenticated username, if available.</param>
    public sealed record Success(AccountId AccountId, string? Username = null) : AuthFlowResult;

    /// <summary>
    /// Authentication was denied.
    /// </summary>
    /// <param name="Reason">The denial reason code to send to the client.</param>
    public sealed record Denied(LoginDeniedReason Reason) : AuthFlowResult;

    /// <summary>
    /// The flow is in progress and managing itself.
    /// </summary>
    public sealed record Pending : AuthFlowResult;
}
