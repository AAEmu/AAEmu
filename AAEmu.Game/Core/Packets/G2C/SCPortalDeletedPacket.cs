using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPortalDeletedPacket(byte portalType, int portalId) : GamePacket(SCOffsets.SCPortalDeletedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(portalType);
        stream.Write(portalId);
        return stream;
    }
}
