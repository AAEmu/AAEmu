using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttendancePacket : GamePacket
{
    private readonly uint _count;
    private readonly DateTime _time;
    private readonly bool _isArchelife;

    public SCAccountAttendancePacket(uint count) : base(SCOffsets.SCAccountAttendancePacket, 5)
    {
        _count = count;
        _time = DateTime.MinValue;
        _isArchelife = false;

    }

    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < _count; i++)
        {
            stream.Write(_time);
            stream.Write(_isArchelife);
        }

        return stream;
    }
}
