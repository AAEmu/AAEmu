using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Reports how many free mission re-rolls have been used (count) for a schedule sort (countType).
/// </summary>
public class SCTodayAssignmentResetCountPacket(uint count, uint countType)
    : GamePacket(SCOffsets.SCTodayAssignmentResetCountPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        stream.Write(countType);
        return stream;
    }
}
