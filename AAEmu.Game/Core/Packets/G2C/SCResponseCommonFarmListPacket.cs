using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The farm list (SC 0x221): every crop the caller has planted, across all farm tabs.
/// </summary>
/// <remarks>
/// <para>
/// The body is <c>u32 maxCount</c>, then a <b>signed</b> <c>s32 count</c>, then that many crop
/// records. One record is <c>u32</c> tab, <c>u32</c> doodad template, <c>u32</c> growing time,
/// <c>u32</c> current phase, the shared quantized world position, and a <c>u64</c> planting time —
/// the same five values the unit state writes for a doodad, in the same order.
/// </para>
/// <para>
/// Two properties of the reader are enforced here rather than assumed. The count is signed, so a
/// negative value carries no records instead of being a very large loop; and the reader honours at
/// most <see cref="CommonFarmListRules.MaxRecordCount"/> records however large the count says, so
/// the write is bounded to the same number. A list that reaches the bound is reported in the log:
/// a player shown a shorter list than they own should be visible, not quietly mistaken for a
/// player with fewer crops.
/// </para>
/// </remarks>
public class SCResponseCommonFarmListPacket(Dictionary<FarmType, List<Doodad>> allPlanted)
    : GamePacket(SCOffsets.SCResponseCommonFarmListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Flattened in tab order first, so the count on the wire and the records after it are two
        // views of the same list. Writing a count and then iterating a different collection is how
        // the two drift apart.
        var records = new List<(FarmType Type, Doodad Doodad)>();
        foreach (var type in Enum.GetValues<FarmType>())
        {
            if (!allPlanted.TryGetValue(type, out var doodadList)) { continue; }
            records.AddRange(doodadList.Select(doodad => (type, doodad)));
        }

        // The count is the caller's own total, so it is never negative in practice. A negative one
        // would be a malformed response rather than an empty one, and it is refused rather than
        // written, because a body whose count and records disagree desynchronises the reader.
        if (!CommonFarmListRules.TryResolveCount(records.Count, records.Count, out var writeCount, out var truncated))
        {
            throw new ArgumentOutOfRangeException(nameof(allPlanted), records.Count,
                "CommonFarmList: the record count is signed and cannot be negative.");
        }

        if (truncated)
        {
            Logger.Warn("CommonFarmList: {0} planted crop(s) exceed the {1}-record response bound; "
                        + "the rest are not sent. The client reads at most {1}.",
                records.Count, CommonFarmListRules.MaxRecordCount);
        }

        // The reader treats a leading total of zero as "this list is not available here" and reports
        // its own reason for refusing, so the two numbers are written from the same resolved count
        // and can never disagree.
        stream.Write(writeCount);
        stream.Write(writeCount);

        for (var index = 0; index < writeCount; index++)
        {
            var (type, doodad) = records[index];
            stream.Write((uint)type);
            stream.Write(doodad.TemplateId);
            stream.Write(doodad.TimeLeft);
            stream.Write(doodad.FuncGroupId);
            stream.WritePosition(doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
            stream.Write(doodad.PlantTime);
        }

        return stream;
    }
}
