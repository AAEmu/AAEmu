using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CAChallengeResponse2PacketHandler
    : ILoginPacketHandler<CAChallengeResponse2Packet>
{
    public void Execute(CAChallengeResponse2Packet packet, LoginConnection connection)
    {
        connection.SendPacket(new ACLoginDeniedPacket(2));
    }
}
