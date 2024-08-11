using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitFactionChangedPacket : GamePacket
{
    private readonly uint _unitId;
    private readonly string _unitName;
    private readonly FactionsEnum _id;
    private readonly FactionsEnum _id2;
    private readonly bool _temp;

    public SCUnitFactionChangedPacket(uint unitId, string unitName, FactionsEnum id, FactionsEnum id2, bool temp)
        : base(SCOffsets.SCUnitFactionChangedPacket, 1)
    {
        _unitId = unitId;
        _unitName = unitName;
        _id = id;
        _id2 = id2;
        _temp = temp;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_unitId);
        stream.Write(_unitName);
        stream.Write((uint)_id);
        stream.Write((uint)_id2);
        stream.Write(_temp);
        return stream;
    }
}
