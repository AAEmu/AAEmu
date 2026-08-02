using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterBoundPacket(
    uint unitObjId,
    uint returnDistrict,
    uint resurrectionDistrict,
    bool returnDistrictChanged)
    : GamePacket(SCOffsets.SCCharacterBoundPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.WriteBc(returnDistrict);
        stream.WriteBc(resurrectionDistrict);
        stream.Write(returnDistrictChanged);
        return stream;
    }
}
