using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Attendance;

public class Attendances
{
    public DateTime AccountAttendance { get; set; }
    public bool Accept { get; set; }


    public Attendances()
    {
    }


    public void Write(PacketStream stream)
    {
        stream.Write(AccountAttendance);
        stream.Write(Accept);
    }
}
