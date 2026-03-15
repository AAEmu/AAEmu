using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Flow for V2 Korea challenge-response authentication.
/// </summary>
public interface IChallenge2AuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Continues the V2 challenge-response flow with the client's computed response.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="ch">The 8 AES-encrypted challenge uint32 values computed by the client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AuthFlowResult> ContinueV2Async(ILoginClient client, uint[] ch, CancellationToken cancellationToken);
}
