using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public sealed class SCExpeditionRecruitmentsGetPacket(
    ExpeditionRecruitmentPage page,
    IReadOnlySet<uint> appliedIds) : GamePacket(SCOffsets.SCExpeditionRecruitmentsGetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)page.Total);
        stream.Write((uint)page.PageSize);
        stream.Write((uint)page.Items.Count);
        foreach (var row in page.Items)
        {
            var expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)row.ExpeditionId);
            stream.Write((int)row.ExpeditionId);
            stream.Write(expedition?.Name ?? string.Empty);
            stream.Write((int)(expedition?.Level ?? 0));
            stream.Write(expedition?.OwnerName ?? string.Empty);
            stream.Write(row.Introduction);
            stream.Write(row.RegisteredAt);
            stream.Write(row.ExpiresAt);
            stream.Write(row.InterestMask);
            stream.Write((uint)(expedition?.Members.Count ?? 0));
            stream.Write(appliedIds.Contains(row.ExpeditionId));
        }
        stream.Write((byte)Math.Min(byte.MaxValue, appliedIds.Count));
        return stream;
    }
}
