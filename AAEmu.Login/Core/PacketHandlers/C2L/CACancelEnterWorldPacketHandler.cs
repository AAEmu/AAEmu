using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CACancelEnterWorldPacketHandler
    : ILoginPacketHandler<CACancelEnterWorldPacket>
{
    public void Execute(CACancelEnterWorldPacket packet, LoginConnection connection)
    {
    }
}
