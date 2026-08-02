using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountAttendancePacket(long[] times = null, bool[] archelife = null)
    : GamePacket(SCOffsets.SCAccountAttendancePacket, 1)
{
    // Body: a FIXED 31-entry array of { "time" u64 (ISerialize vtbl+0x78), "isArchelife" bool (vtbl+0xF8) }.
    // length prefix. Represents the monthly attendance calendar; zero entries = nothing claimed.
    private const int Days = 31;
    private readonly long[] _times = times ?? new long[Days];
    private readonly bool[] _archelife = archelife ?? new bool[Days];

    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < Days; i++)
        {
            stream.Write(_times[i]);
            stream.Write(_archelife[i]);
        }
        return stream;
    }
}
