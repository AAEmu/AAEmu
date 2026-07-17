using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyTitlePacket(uint unitId, byte role, string owner, string title)
    : GamePacket(SCOffsets.SCFamilyTitlePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(role);
        stream.Write(owner); // TODO max length 128
        stream.Write(title); // TODO max length 104
        return stream;
    }
}
