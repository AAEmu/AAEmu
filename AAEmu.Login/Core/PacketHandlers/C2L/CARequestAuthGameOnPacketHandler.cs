using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestAuthGameOnPacketHandler
    : ILoginPacketHandler<CARequestAuthGameOnPacket>
{
    public void Execute(CARequestAuthGameOnPacket packet, LoginConnection connection)
    {
    }
}
