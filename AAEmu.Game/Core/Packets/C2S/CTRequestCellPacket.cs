using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTRequestCellPacket() : StreamPacket(CTOffsets.CTRequestCellPacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override void Read(PacketStream stream)
    {
        // i(i32), x(u32), y(u32).
        var instanceId = stream.ReadInt32();
        var x = stream.ReadUInt32();
        var y = stream.ReadUInt32();

        Logger.Debug($"CTRequestCellPacket #.{instanceId} ({x},{y})");
        StreamManager.RequestCell(Connection, instanceId, x, y);
    }
}
