using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SCBuffUpdated (0x0EE) — charge/stack/elapsed refresh. Body matches WZBuffUpdated (0x040):
/// bc unitId + i32 buffIndex + u32 stack + u32 charged + i32 elapsedTime + u8 reason
/// </summary>
public class SCBuffUpdatedPacket(
    uint unitId,
    int buffIndex,
    uint stack,
    uint charged,
    int elapsedTime,
    byte reason) : GamePacket(SCOffsets.SCBuffUpdatedPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(buffIndex);
        stream.Write(stack);
        stream.Write(charged);
        stream.Write(elapsedTime);
        stream.Write(reason);
        return stream;
    }
}
