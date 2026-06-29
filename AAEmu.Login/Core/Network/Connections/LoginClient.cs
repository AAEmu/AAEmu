using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Implementation of <see cref="ILoginClient"/> that wraps an <see cref="ILoginConnection"/>
/// and constructs the appropriate packets.
/// </summary>
public sealed class LoginClient(ILoginConnection connection) : ILoginClient
{
    public async ValueTask SendAuthSuccessAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        // 10.0.2.13 feature set: chMaxCountLimit must be >= 1 or the client blocks character creation. TODO: Make configurable.
        const byte ChCountLimit = 6;      // base creatable character count
        const byte ChMaxCountLimit = 6;   // max characters per account
        const byte ChCountWorldLimit = 6; // max characters per world

        await connection.SendPacketAsync(
            new ACJoinResponsePacket(JoinResponseReason.Success,
                new AfsValue(ChCountLimit, ChMaxCountLimit, ChCountWorldLimit)),
            cancellationToken);
        await connection.SendPacketAsync(new ACAuthResponsePacket(accountId, 6), cancellationToken);
    }

    public ValueTask SendAuthDeniedAsync(LoginDeniedReason reason, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACLoginDeniedPacket(reason), cancellationToken);

    public ValueTask SendWorldListAsync(WorldListResult worldList, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACWorldListPacket(worldList.GameServers, worldList.Characters), cancellationToken);

    public ValueTask SendChallengeAsync(uint salt, uint[] hc, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACChallengePacket(salt, hc), cancellationToken);

    public ValueTask SendChallenge2Async(int round, string salt, uint[] hc, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACChallenge2Packet(round, salt, hc), cancellationToken);

    public ValueTask SendOtpPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken)
    {
        var packet = currentTry == 0
            ? ACEnterOtpPacket.CreateInitialPacket()
            : ACEnterOtpPacket.CreateFailurePacket(maximumTries, currentTry);
        return connection.SendPacketAsync(packet, cancellationToken);
    }

    public ValueTask SendCertPromptAsync(int maximumTries, int currentTry, CancellationToken cancellationToken)
    {
        var packet = currentTry == 0
            ? ACEnterPcCertPacket.CreateInitialPacket()
            : ACEnterPcCertPacket.CreateFailurePacket(maximumTries, currentTry);
        return connection.SendPacketAsync(packet, cancellationToken);
    }

    public ValueTask SendArsPromptAsync(string sessionCode, TimeSpan timeout, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACShowArsPacket(sessionCode, timeout), cancellationToken);

    public ValueTask SendWorldCookieAsync(GameServer server, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACWorldCookiePacket(connection, server), cancellationToken);

    public ValueTask SendEnterWorldDeniedAsync(byte reason, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACEnterWorldDeniedPacket(reason), cancellationToken);
}
