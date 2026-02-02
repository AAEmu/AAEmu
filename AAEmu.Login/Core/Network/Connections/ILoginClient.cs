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
    ValueTask SendChallengeAsync(CancellationToken cancellationToken);
    ValueTask SendOtpPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken);
    ValueTask SendCertPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken);
    ValueTask SendArsPromptAsync(string sessionCode, TimeSpan timeout, CancellationToken cancellationToken);
    ValueTask SendWorldCookieAsync(GameServer server, CancellationToken cancellationToken);
    ValueTask SendEnterWorldDeniedAsync(byte reason, CancellationToken cancellationToken);
}
