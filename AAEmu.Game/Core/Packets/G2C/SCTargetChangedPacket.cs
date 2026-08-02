using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// the client run out of body mid-read ("not enough buffer for bc"), desyncing the whole SC stream.
/// One packet covers both the receiving player's own target and any other unit's aggro target.
/// </summary>
public class SCTargetChangedPacket(uint unitId, uint targetId)
    : GamePacket(SCOffsets.SCTargetChangedPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WriteBc(targetId);
        return stream;
    }
}
