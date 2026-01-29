using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAEnterWorldPacket"/> which is sent by the client to request entering the game world.
/// </summary>
public class CAEnterWorldPacketHandler(IGameController gameController) : ILoginPacketHandler<CAEnterWorldPacket>
{
    public Task Execute(CAEnterWorldPacket packet, ILoginConnection connection, CancellationToken cancellationToken)
    {
        gameController.RequestEnterWorld(connection, packet.GsId);
        return Task.CompletedTask;
    }
}
