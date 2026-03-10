using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Represents an authentication flow that can be started and may require
/// additional steps before completing.
/// </summary>
/// <remarks>
/// Flows are pure decision-makers that return <see cref="AuthFlowResult"/> values.
/// <see cref="ILoginSession"/> manages the lifecycle: setting the current flow on
/// <see cref="AuthFlowResult.Pending"/>, and clearing it on
/// <see cref="AuthFlowResult.Success"/> or <see cref="AuthFlowResult.Denied"/>.
/// </remarks>
public interface IAuthenticationFlow
{
    /// <summary>
    /// Starts the authentication flow.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The result of starting the flow:
    /// <list type="bullet">
    ///   <item><see cref="AuthFlowResult.Success"/> - authentication completed successfully</item>
    ///   <item><see cref="AuthFlowResult.Denied"/> - authentication failed</item>
    ///   <item><see cref="AuthFlowResult.Pending"/> - flow needs additional input</item>
    /// </list>
    /// </returns>
    Task<AuthFlowResult> StartAsync(ILoginClient client, CancellationToken cancellationToken);
}
