using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttributePacket : GamePacket
{
    private readonly byte _AccountAttributeKind;
    private readonly uint _extraKind;
    private readonly byte _worldId;
    private readonly uint _count;
    private readonly DateTime _startDate;
    private readonly DateTime _endData;

    public SCAccountAttributePacket() : base(SCOffsets.SCAccountAttributePacket, 5)
    {
        _AccountAttributeKind = 2;
        _extraKind = 6;
        _worldId = 0xFF;
        _count = 1;
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
            stream.Write(_count);
            stream.Write(_startDate);
            stream.Write(_endData);
        }
        return stream;
    }
}
