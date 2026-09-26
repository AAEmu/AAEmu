using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One activity's row in <see cref="SCSailingActivityListPacket"/>.</summary>
/// <param name="ActivityId">Signed 32-bit activity id, as the client serializes it.</param>
/// <param name="StartTime">64-bit start stamp; <see cref="SailingActivityWindow.UnresolvedStamp"/> when unknown.</param>
/// <param name="EndTime">64-bit end stamp; <see cref="SailingActivityWindow.UnresolvedStamp"/> when unknown.</param>
public readonly record struct SailingActivityListRow(int ActivityId, ulong StartTime, ulong EndTime);

/// <summary>
/// The sailing activities currently offered to the client: a signed 32-bit count followed by that
/// many rows, each a signed 32-bit <c>activityId</c> and two 64-bit stamps.
/// </summary>
/// <remarks>
/// Nothing constructs this packet yet. Building the rows needs the activity window resolved from
/// content, and the relative-window forms in that content have no recovered meaning — see
/// <see cref="SailingActivityWindow"/>.
/// </remarks>
public class SCSailingActivityListPacket(SailingActivityListRow[] rows)
    : GamePacket(SCOffsets.SCSailingActivityListPacket, 1)
{
    /// <summary>
    /// The client reads these rows into a <b>fixed array of 32</b> and its copy loop carries no bound
    /// check, so a 33rd row would be written past the end of its buffer. The limit is therefore the
    /// client's, not a defensive bound of ours.
    /// </summary>
    /// <remarks>
    /// Corroborated arithmetically by the recovered struct: the count sits at <c>+0x14</c>, 32
    /// four-byte ids run from <c>+0x18</c>, and the first time field begins at <c>+0x98</c>.
    /// <c>0x18 + 32*4 == 0x98</c> exactly.
    /// </remarks>
    public const int MaximumRows = 32;

    public override PacketStream Write(PacketStream stream)
    {
        var payload = rows ?? [];
        if (payload.Length > MaximumRows)
            throw new ArgumentOutOfRangeException(nameof(rows), payload.Length, $"a sailing-activity list may carry at most {MaximumRows} rows");

        stream.Write(payload.Length);
        foreach (var row in payload)
        {
            stream.Write(row.ActivityId);
            stream.Write(row.StartTime);
            stream.Write(row.EndTime);
        }

        return stream;
    }
}
