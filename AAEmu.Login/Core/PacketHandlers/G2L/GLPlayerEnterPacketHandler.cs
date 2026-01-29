using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLPlayerEnterPacket"/> which is sent by the game server when a player enters the game world.
/// </summary>
public class GLPlayerEnterPacketHandler(IGameController gameController, ILoginConnectionTable loginConnectionTable)
    : IInternalPacketHandler<GLPlayerEnterPacket>
{
    public async Task Execute(GLPlayerEnterPacket packet, InternalConnection connection,
        CancellationToken cancellationToken)
    {
        var loginConnection = loginConnectionTable.GetConnection(packet.ConnectionId);
        await gameController.EnterWorldAsync(loginConnection!, packet.GsId, packet.Result);
    }
}
