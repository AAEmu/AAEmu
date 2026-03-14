using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Abstraction for sending response packets to the client.
/// Decouples business logic from protocol-level packet construction.
/// </summary>
public interface ILoginClient
{
    ValueTask SendAuthSuccessAsync(AccountId accountId, CancellationToken cancellationToken);
    ValueTask SendAuthDeniedAsync(LoginDeniedReason reason, CancellationToken cancellationToken);
    ValueTask SendWorldListAsync(WorldListResult worldList, CancellationToken cancellationToken);
    /// <summary>
    /// Sends a V1 Korea challenge packet (<c>ACChallengePacket</c>).
    /// V1 is not enabled in production — SHA-1 without a per-account salt is ~17M× weaker than PBKDF2@600k.
    /// Preserved for protocol completeness only.
    /// </summary>
    ValueTask SendChallengeAsync(uint salt, uint[] hc, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a V2 Korea challenge packet (<c>ACChallenge2Packet</c>).
    /// The client uses <paramref name="round"/> and <paramref name="salt"/> to derive the AES-256 key
    /// via sha256_crypt, then computes the 8-uint challenge response.
    /// </summary>
    ValueTask SendChallenge2Async(int round, string salt, uint[] hc, CancellationToken cancellationToken);
    ValueTask SendOtpPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken);
    ValueTask SendCertPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken);
    ValueTask SendArsPromptAsync(string sessionCode, TimeSpan timeout, CancellationToken cancellationToken);
    ValueTask SendWorldCookieAsync(GameServer server, CancellationToken cancellationToken);
    ValueTask SendEnterWorldDeniedAsync(byte reason, CancellationToken cancellationToken);
}
