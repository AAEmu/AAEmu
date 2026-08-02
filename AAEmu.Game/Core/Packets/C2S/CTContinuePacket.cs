using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTContinuePacket() : StreamPacket(CTOffsets.CTContinuePacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32();
        var next = stream.ReadUInt32();
        StreamManager.ContinueCell(Connection, id, next);
    }
}
