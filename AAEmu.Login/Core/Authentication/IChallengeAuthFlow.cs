using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Authentication flow for V1 Korea challenge-response (not enabled in production).
/// </summary>
/// <remarks>
/// <b>NOTE:</b> V1 challenge authentication is not enabled — SHA-1 without a per-account salt is
/// approximately 17 million times weaker than PBKDF2@600k. Implementations must always deny
/// in <see cref="ContinueAsync"/> when called (the server never issues <c>ACChallengePacket</c>).
/// This interface is preserved for protocol completeness only.
/// </remarks>
public interface IChallengeAuthFlow : IAuthenticationFlow
{
    /// <summary>
    /// Continues the V1 challenge-response flow with the parsed packet fields.
    /// Implementations must return <see cref="AuthFlowResult.Denied"/> immediately —
    /// V1 is never issued so this should never be reached in production.
    /// </summary>
    /// <param name="client">The login client for sending responses.</param>
    /// <param name="ch">The 4 AES-encrypted challenge uint32 values from the client.</param>
    /// <param name="pw">The 32 raw bytes of the AES-encrypted plaintext password (V1 only).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AuthFlowResult> ContinueAsync(ILoginClient client, uint[] ch, byte[] pw,
        CancellationToken cancellationToken);
}
