using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSpecialtyCurrentPacket : GamePacket
{
    private ushort _fromZoneGroup;
    private ushort _toZoneGroup;
    private List<(uint, uint)> _results;

    public SCSpecialtyCurrentPacket(ushort fromZoneGroup, ushort toZoneGroup, List<(uint, uint)> results) : base(SCOffsets.SCSpecialtyCurrentPacket, 1)
    {
        _fromZoneGroup = fromZoneGroup;
        _toZoneGroup = toZoneGroup;
        _results = results;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_results.Count);
        stream.Write(_fromZoneGroup);
        stream.Write(_toZoneGroup);
        foreach (var (itemId, rate) in _results)
        {
            stream.Write(itemId);
            stream.Write(rate);
        }
        return stream;
    }
}
