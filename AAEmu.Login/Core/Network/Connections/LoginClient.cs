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
        const byte MaxCharactersPerAccount = 2; // TODO: Make configurable
        const byte AdditionalCharactersPerServer = 2; // TODO: Make configurable
        const ushort AdditionalData = 22; // TODO: Make configurable
        const bool IsPreSelectCharacterPeriod = false; // TODO: Make configurable

        await connection.SendPacketAsync(new ACJoinResponsePacket((byte)1, JoinResponseReason.Success, new AfsValue(MaxCharactersPerAccount, AdditionalCharactersPerServer, AdditionalData, IsPreSelectCharacterPeriod)), cancellationToken);
        await connection.SendPacketAsync(new ACAuthResponsePacket(accountId.Value, 0), cancellationToken);
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
