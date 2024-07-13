using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDbAttendanceTimePacket : GamePacket
{
    private readonly bool _result;
    private readonly DateTime _dbAttendanceTime;

    public SCDbAttendanceTimePacket(bool result, DateTime dbAttendanceTime) : base(SCOffsets.SCDbAttendanceTimePacket, 5)
    {
        _result = result;
        _dbAttendanceTime = dbAttendanceTime;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_result);
        stream.Write(_dbAttendanceTime);

        return stream;
    }
}
