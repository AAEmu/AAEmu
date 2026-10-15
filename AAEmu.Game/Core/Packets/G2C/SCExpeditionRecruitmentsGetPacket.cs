using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionRecruitmentsGetPacket(
    ExpeditionRecruitmentPage page,
    IReadOnlySet<uint> appliedIds) : GamePacket(SCOffsets.SCExpeditionRecruitmentsGetPacket, 1)
{
    /// <summary>Client deserializer always reads this many recruitment rows after count.</summary>
    public const int SlotCount = ExpeditionRecruitmentService.PageSize;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)page.Total);
        stream.Write((uint)page.Page);
        stream.Write((uint)page.Items.Count);
        for (var i = 0; i < SlotCount; i++)
        {
            if (i < page.Items.Count)
                WriteRow(stream, page.Items[i], appliedIds);
            else
                WriteEmptyRow(stream);
        }

        stream.Write((byte)Math.Min(byte.MaxValue, appliedIds.Count));
        return stream;
    }

    private static void WriteRow(PacketStream stream, ExpeditionRecruitment row, IReadOnlySet<uint> appliedIds)
    {
        var expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)row.ExpeditionId);
        stream.Write((int)row.ExpeditionId);
        stream.Write(expedition?.Name ?? string.Empty);
        stream.Write((int)(expedition?.Level ?? 0));
        stream.Write(expedition?.OwnerName ?? string.Empty);
        stream.Write(row.Introduction);
        // Listing clocks are Unix seconds (low 32 bits used for remaining time). A 64-bit
        // Windows file-time value overflows that math and the board treats every row as expired.
        stream.Write(Helpers.UnixTime(ServerCalendar.AsUtc(row.RegisteredAt)));
        stream.Write(Helpers.UnixTime(ServerCalendar.AsUtc(row.ExpiresAt)));
        stream.Write(row.InterestMask);
        stream.Write((uint)(expedition?.Members.Count ?? 0));
        stream.Write(appliedIds.Contains(row.ExpeditionId));
    }

    private static void WriteEmptyRow(PacketStream stream)
    {
        stream.Write(0);
        stream.Write(string.Empty);
        stream.Write(0);
        stream.Write(string.Empty);
        stream.Write(string.Empty);
        stream.Write(0L);
        stream.Write(0L);
        stream.Write((short)0);
        stream.Write(0u);
        stream.Write(false);
    }
}
