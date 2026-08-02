using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTCancelCellPacket() : StreamPacket(CTOffsets.CTCancelCellPacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override void Read(PacketStream stream)
    {
        // i(i32 instance id), x(u32 cell), y(u32 cell).
        var instanceId = stream.ReadInt32();
        var x = stream.ReadUInt32();
        var y = stream.ReadUInt32();

        AAEmu.Game.Core.Managers.World.StreamManager.CancelCell(Connection, instanceId, x, y);
    }
}
