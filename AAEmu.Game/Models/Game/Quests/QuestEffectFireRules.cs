namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Progress act QuestActObjEffectFire: the player fires skill effect effect_id after accept.
/// quest_act_obj_effect_fires has 154 rows, 149 enabled Progress rows over 112 quests. effect_id is
/// effects.id (146 distinct: 69 InteractionEffect, 33 SpecialEffect, 29 BuffEffect, 8 OpenPortalEffect,
/// 3 RestoreManaEffect, 2 GainLootPackItemEffect, 1 CraftEffect, 1 BubbleEffect). 134 ids are reached
/// through skill_effects.effect_id, the other 12 only through buff_triggers.effect_id; none through
/// plot_effects, which carries actual_id/actual_type instead of an effects.id. The client reader
/// LoadQuestActObjEffectFireDescs (x2game-dev.dll FUN_39b04c80) reads id, count, effect_id,
/// quest_act_obj_alias_id, team_share, use_alias.
/// </summary>
public static class QuestEffectFireRules
{
    public static bool Counts(uint actEffectId, uint firedEffectId)
        => actEffectId != 0 && actEffectId == firedEffectId;

    /// <summary>
    /// team_share forwards the fire to team-mates the way QuestActObjInteraction does. Only the
    /// firing character forwards, so a forwarded event stops at the team-mate's own handler.
    /// </summary>
    public static bool SharesWithTeam(bool teamShare, uint ownerCharacterId, uint sourceCharacterId)
        => teamShare && ownerCharacterId != 0 && ownerCharacterId == sourceCharacterId;

    /// <summary>
    /// A forwarded fire only reaches a team-mate the kill credit would reach: the same zone and
    /// LootingContainer.MaxLootingRange, the range Npc.DoDie applies to tag-team members.
    /// </summary>
    public static bool SharesAtRange(uint ownerZoneId, uint memberZoneId, float distance, float maxRange)
        => ownerZoneId == memberZoneId && distance <= maxRange;
}
