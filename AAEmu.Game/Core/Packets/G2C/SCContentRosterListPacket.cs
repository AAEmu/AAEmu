using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The account's saved content rosters. Each row is the same info record the save answer writes.
/// </summary>
/// <remarks>
/// i32 count, then per roster: bool cached, i64 id, i64 recordTime, string title.
/// The title reader stops at 80 bytes.
/// </remarks>
public class SCContentRosterListPacket(IReadOnlyList<ContentRosterListRow> rows)
    : GamePacket(SCOffsets.SCContentRosterListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var list = rows ?? [];
        stream.Write(list.Count);
        foreach (var row in list)
        {
            stream.Write(false);
            stream.Write(row.Id);
            stream.Write(row.RecordTime);
            stream.Write(row.Title ?? string.Empty);
        }

        return stream;
    }
}

public readonly record struct ContentRosterListRow(long Id, long RecordTime, string Title);
