using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// i32-ratio/i64-time records.
/// </summary>
public class SCSpecialtyRecordsPacket(
    ushort zoneGroupId,
    uint itemId,
    IReadOnlyList<SpecialtyMarketRecord> records) : GamePacket(SCOffsets.SCSpecialtyRecordsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (records.Count > 256)
            throw new ArgumentOutOfRangeException(nameof(records), "A specialty history response can contain at most 256 records.");

        stream.Write((uint)records.Count);
        stream.Write(zoneGroupId);
        stream.Write(itemId);
        foreach (var record in records)
            stream.Write(record);
        return stream;
    }
}
