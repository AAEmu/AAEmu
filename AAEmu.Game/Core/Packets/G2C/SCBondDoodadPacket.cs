using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBondDoodadPacket(uint unitObjId, BondDoodad bond) : GamePacket(SCOffsets.SCBondDoodadPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitObjId);
        stream.Write(bond);
        return stream;
    }
}
