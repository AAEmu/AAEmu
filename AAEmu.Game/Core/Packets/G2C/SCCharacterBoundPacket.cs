using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterBoundPacket(
    uint unitObjId,
    uint returnDistrict,
    uint resurrectionDistrict,
    bool returnDistrictChanged,
    uint id,
    string name,
    uint zoneKey,
    Vector3 pos,
    float zRot,
    bool isFavorite)
    : GamePacket(SCOffsets.SCCharacterBoundPacket, 1)
{
    // Not sure what this should be

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.Write(returnDistrict);
        stream.Write(resurrectionDistrict);
        stream.Write(returnDistrictChanged);
        stream.Write(id);
        stream.Write(name, true);
        stream.Write(zoneKey);
        stream.Write(pos.X);
        stream.Write(pos.Y);
        stream.Write(pos.Z);
        stream.Write(zRot);
        stream.Write(isFavorite);
        return stream;
    }
}
