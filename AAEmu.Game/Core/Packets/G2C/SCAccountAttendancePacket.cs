using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Attendance;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttendancePacket : GamePacket
{
    private readonly List<Attendances> _attendances;

    public SCAccountAttendancePacket(List<Attendances> attendances) : base(SCOffsets.SCAccountAttendancePacket, 5)
    {
        _attendances = attendances;
    }

    public override PacketStream Write(PacketStream stream)
    {
        foreach (var attendance in _attendances)
        {
            attendance.Write(stream);
        }

        return stream;
    }
}
