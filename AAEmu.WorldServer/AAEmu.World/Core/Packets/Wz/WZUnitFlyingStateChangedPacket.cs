using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitFlyingState (0x036) — sets a unit's flying state on Zone.
///
/// ground actor and physics pulls it down.
/// </summary>
public class WZUnitFlyingStateChangedPacket(uint bcId, bool flying) : ZonePacket(WzOpcodes.UnitFlyingStateChanged)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(bcId);
        stream.Write(flying);
    }
}
