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
        await connection.SendPacketAsync(new ACJoinResponsePacket(0, 6), cancellationToken);
        await connection.SendPacketAsync(new ACAuthResponsePacket(accountId, 6), cancellationToken);
    }

    public ValueTask SendAuthDeniedAsync(LoginDeniedReason reason, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACLoginDeniedPacket(reason), cancellationToken);

    public ValueTask SendWorldListAsync(WorldListResult worldList, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACWorldListPacket(worldList.GameServers, worldList.Characters), cancellationToken);

    public ValueTask SendWorldCookieAsync(GameServer server, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACWorldCookiePacket(connection, server), cancellationToken);

    public ValueTask SendEnterWorldDeniedAsync(byte reason, CancellationToken cancellationToken) =>
        connection.SendPacketAsync(new ACEnterWorldDeniedPacket(reason), cancellationToken);
}
