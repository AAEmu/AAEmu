using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// world_message_effects — broadcasts a world notice (PvP kill / kill-streak / hero announcements). The
// `message` column is a localized text key the client resolves.
public class WorldMessageEffect : EffectTemplate
{
    public bool ZoneGroupOnly { get; set; }
    public string Message { get; set; }
    public bool ZoneGroupWarState { get; set; }
    public int FactionScopeId { get; set; }
    public int KillStreakCount { get; set; }
    public bool KillHero { get; set; }
    public string IconKey { get; set; }
    public bool ChatMsg { get; set; }
    public bool NameWithForeignWorld { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (string.IsNullOrEmpty(Message))
            return;

        // messageType bit: 1 = also surface in chat. ChatMsg drives it; source 0 = generic world message.
        var packet = new SCWorldMessagePacket(0, ChatMsg ? (byte)1 : (byte)0, Message);
        if (packetBuilder != null)
        {
            packetBuilder.AddPacket(packet);
            return;
        }

        // zone_group_only (732 of 945 rows) wants a zone-group-wide audience and faction_scope_id (38 rows at
        // 1) a faction-scoped one; kill_streak_count (18 rows at 2..5) is a streak announcement and
        // kill_hero/zone_group_war_state/icon_key/name_with_foreign_world the rest of that family. None of
        // them has a consumer: the only broadcast path this effect has is the caster's visible range, and
        // there is no zone-group or faction-scoped world-message channel to send on (WorldIntegration
        // relays unit, buff, spawn, aggro and gimmick traffic, not chat). Inventing a fan-out would put a
        // message in front of players the row did not author it for, which is the one thing these four
        // columns exist to prevent, so the audience stays as it was and the columns stay loaded and unread.
        Logger.Debug(
            "WorldMessageEffect {0}: zoneGroupOnly={1} factionScope={2} killStreak={3} - no scoped broadcast path",
            Id, ZoneGroupOnly, FactionScopeId, KillStreakCount);

        caster.BroadcastPacket(packet, true);
    }
}
