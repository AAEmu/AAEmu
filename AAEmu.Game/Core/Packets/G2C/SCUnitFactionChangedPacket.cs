using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitFactionChangedPacket(uint unitId, string unitName, FactionsEnum id, FactionsEnum id2, bool temp)
    : GamePacket(SCOffsets.SCUnitFactionChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(unitName);
        stream.Write((uint)id);
        stream.Write((uint)id2);
        stream.Write(temp);
        return stream;
    }
}
