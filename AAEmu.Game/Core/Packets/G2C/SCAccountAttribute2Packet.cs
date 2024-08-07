using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttribute2Packet : GamePacket
{
    private readonly byte _AccountAttributeKind;
    private readonly uint _extraKind;
    private readonly byte _worldId;
    private readonly uint _count;
    private readonly DateTime _startDate;
    private readonly DateTime _endData;

    public SCAccountAttribute2Packet() : base(SCOffsets.SCAccountAttribute2Packet, 5)
    {
        _AccountAttributeKind = 1;
        _extraKind = 0;
        _worldId = 0x1;
        _count = 0;
        _startDate = DateTime.Now;
        _endData = DateTime.MinValue;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_AccountAttributeKind);
        stream.Write(_extraKind);
        stream.Write(_worldId);
        stream.Write(_count);
        stream.Write(_startDate);
        stream.Write(_endData);
        return stream;
    }
}
