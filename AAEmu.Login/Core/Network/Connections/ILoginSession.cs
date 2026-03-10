using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Manages the login flow state machine for a client connection.
/// </summary>
public interface ILoginSession
{
    /// <summary>
    /// Gets the underlying connection for this session.
    /// </summary>
    ILoginConnection Connection { get; }

    /// <summary>
    /// Gets the client abstraction for sending response packets.
    /// </summary>
    ILoginClient Client { get; }

    /// <summary>
    /// Gets the current state of the login session.
    /// </summary>
    LoginState State { get; }

    /// <summary>
    /// Gets the current authentication flow, if any (read-only, for diagnostics).
    /// </summary>
    IAuthenticationFlow? CurrentAuthFlow { get; }

    /// <summary>
    /// Sends a packet to the client.
    /// </summary>
    ValueTask SendPacketAsync(LoginPacket packet, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a new authentication flow. Validates that the session is in the Connected state,
    /// transitions to Authenticating, and processes the flow result.
    /// Sends auth success/denied packets internally.
    /// </summary>
    /// <param name="flow">The authentication flow to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AuthenticateAsync(IAuthenticationFlow flow, CancellationToken cancellationToken);

    /// <summary>
    /// Continues an in-progress authentication flow. Validates that the session is in the Authenticating state
    /// and that the current flow matches the expected type.
    /// Sends auth success/denied packets internally.
    /// </summary>
    /// <typeparam name="TFlow">The expected flow type.</typeparam>
    /// <param name="step">The step to execute on the flow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ContinueAuthAsync<TFlow>(Func<TFlow, Task<AuthFlowResult>> step, CancellationToken cancellationToken)
        where TFlow : class, IAuthenticationFlow;

    /// <summary>
    /// Initiates entering the world on the specified game server.
    /// Sends world cookie or denied packets internally via a background task.
    /// </summary>
    /// <param name="gsId">The game server to enter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitiateEnterWorldAsync(GameServerId gsId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels an in-progress enter world request.
    /// </summary>
    void CancelEnterWorld();

    /// <summary>
    /// Completes a pending enter world request with the result from the game server.
    /// </summary>
    /// <param name="gsId">The game server that responded.</param>
    /// <param name="result">The result code (0 = success).</param>
    void CompleteEnterWorldRequest(GameServerId gsId, byte result);

    /// <summary>
    /// Marks the session as disconnected and cancels any pending operations.
    /// </summary>
    Task DisconnectAsync();
}
