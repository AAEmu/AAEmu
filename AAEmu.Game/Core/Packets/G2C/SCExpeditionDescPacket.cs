using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The guild info panel's descriptor - level/exp/notice and the rest of X2::ExpeditionDesc. No
/// client-side request opcode exists for this data, so it must be server-pushed, matching the
/// existing SendExpeditionInfo pattern (login, create, invite-accept).
/// </summary>
public sealed class SCExpeditionDescPacket(Expedition expedition) : GamePacket(SCOffsets.SCExpeditionDescPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // FactionDesc base portion (id, motherId, name, owner, ..., integrationFaction) - shared with
        // SCExpeditionListPacket/SCFactionCreatedPacket.
        stream.Write(expedition);

        stream.Write((int)expedition.Level);
        stream.Write((int)expedition.Exp);
        // TODO: do not reorder these 4 fields (protectDate/warDeposit/dailyExp/lastExpUpdateTime) without
        // live-debugged evidence - a prior reorder attempt broke the guild protection-time display.
        stream.Write(expedition.WarEndsAt ?? expedition.WarProtectedUntil ?? DateTime.UtcNow); // protectDate
        stream.Write(0);                   // warDeposit - not tracked
        stream.Write(0);                   // dailyExp - not tracked separately from Exp yet
        stream.Write(DateTime.UtcNow);     // lastExpUpdateTime
        stream.Write(expedition.Interest);
        stream.Write(Truncate(expedition.Notice, 800));
        stream.Write(0);                   // win - GuildWar win/lose/draw totals not tracked yet
        stream.Write(0);                   // lose
        stream.Write(0);                   // draw
        // Client-cached gate on the guild-level-up button; must be 0xff ("no restriction") or the client
        // silently blocks every level-up attempt without sending a packet or showing an error.
        stream.Write(0xff);
        stream.Write(0L);                  // unconfirmed field
        stream.Write((long)expedition.TotalContributionPoint); // guild-level total, sum of all members' own ContributionPoint
        stream.Write(0);                   // dailyContributionPoint
        stream.Write(DateTime.UtcNow);     // lastContributionPointAdded
        stream.Write(DateTime.UtcNow);     // lastAssignmentUpdateTime
        return stream;
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, maxLength)];
}
