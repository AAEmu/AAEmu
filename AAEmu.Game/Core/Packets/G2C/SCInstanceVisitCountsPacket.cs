using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstanceVisitCountsPacket(IReadOnlyList<InstanceVisitCountRecord> records = null)
    : GamePacket(SCOffsets.SCInstanceVisitCountsPacket, 1)
{
    private readonly IReadOnlyList<InstanceVisitCountRecord> _records = records ?? [];

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_records.Count);
        foreach (var row in _records)
            WriteVisitRow(stream, row);

        return stream;
    }

    internal static void WriteVisitRow(PacketStream stream, InstanceVisitCountRecord row)
    {
        stream.Write(row.ZoneGroupId);
        stream.Write(row.InstanceCatalogId);
        stream.Write(row.UsedCount);
        stream.Write(row.ResetCount);
        stream.Write(row.PermittedCount);
    }
}
