using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CAEnterWorldPacketHandler(IGameController gameController)
    : ILoginPacketHandler<CAEnterWorldPacket>
{
    public void Execute(CAEnterWorldPacket packet, LoginConnection connection)
    {
        gameController.RequestEnterWorld(connection, packet.GsId);
    }
}
