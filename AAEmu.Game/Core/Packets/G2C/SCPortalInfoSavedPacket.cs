using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPortalInfoSavedPacket(Portal portal) : GamePacket(SCOffsets.SCPortalInfoSavedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(portal);
        return stream;
    }
}
