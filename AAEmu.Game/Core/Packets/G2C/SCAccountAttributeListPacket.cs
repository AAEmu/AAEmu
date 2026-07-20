using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributeListPacket : GamePacket
{
    private readonly byte _AccountAttributeKind;
    private readonly uint _extraKind;
    private readonly byte _worldId;
    private readonly uint _count;
    private readonly uint _count2;
    private readonly DateTime _startDate;
    private readonly DateTime _endData;

    public SCAccountAttributeListPacket() : base(SCOffsets.SCAccountAttributeListPacket, 5)
    {
        _AccountAttributeKind = 1;
        _extraKind = 0;
        _worldId = 0xFF;
        _count = 1;
        _count2 = 0;
        _startDate = DateTime.Now;
        _endData = DateTime.MinValue;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_count);
        for (var i = 0; i < _count; i++)
        {
            stream.Write(_AccountAttributeKind); // chatTypeGroup
            stream.Write(_extraKind);
            stream.Write(_worldId);
            stream.Write(_count2);
            stream.Write(_startDate);
            stream.Write(_endData);
        }
        return stream;
    }
}
