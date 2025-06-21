using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestAuthTencentPacketHandler
    : ILoginPacketHandler<CARequestAuthTencentPacket>
{
    public void Execute(CARequestAuthTencentPacket packet, LoginConnection connection)
    {
    }
}
