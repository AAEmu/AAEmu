namespace AAEmu.Game.Models.Game.Skills.Static;

/// <summary>
/// Extracted enum of skill results, might not be correct
/// </summary>
public enum SkillResult : byte
{
    Success = 0x0,
    Failure = 0x1,
    SourceDied = 0x2,
    SourceAlive = 0x3,
    TargetDied = 0x4,
    TargetDestroyed = 0x5,
    TargetAlive = 0x6,
    OnCasting = 0x7,
    CooldownTime = 0x8,
    NoTarget = 0x9,
    LackHealth = 0xA,
    LackMana = 0xB,
    Obstacle = 0xC,
    OutofHeight = 0xD,
    TooCloseRange = 0xE,
    TooFarRange = 0xF,
    OutofAngle = 0x10,
    CannotCastInCombat = 0x11,
    CannotCastWhileMoving = 0x12,
    CannotCastInStun = 0x13,
    CannotCastWhileWalking = 0x14,
    CannotCastInSwimming = 0x15,
    BlankMinded = 0x16,
    Silence = 0x17,
    Crippled = 0x18,
    CannotCastInChanneling = 0x19,
    CannotCastInPrison = 0x1A,
    NeedStealth = 0x1B,
    NeedNocombatTarget = 0x1C,
    TargetImmune = 0x1D,
    InvalidSkill = 0x1E,
    InactiveAbility = 0x1F,
    NotEnoughAbilityLevel = 0x20,
    InvalidSource = 0x21,
    InvalidTarget = 0x22,
    InvalidLocation = 0x23,
    NeedReagent = 0x24,
    ItemLocked = 0x25,
    NeedMoney = 0x26,
    NeedLaborPower = 0x27,
    SourceIsHanging = 0x28,
    SourceIsRiding = 0x29,
    HigherBuff = 0x2A,
    NotPvpArea = 0x2B,
    NotNow = 0x2C,
    NoPerm = 0x2D,
    BagFull = 0x2E,
    ProtectedFaction = 0x2F,
    ProtectedLevel = 0x30,
    UnitReqsOrFail = 0x31,
    SkillReqFail = 0x32,
    BackpackOccupied = 0x33,
    ObstacleForSpawnDoodad = 0x34,
    CannotSpawnDoodadInHouse = 0x35,
    CannotUseForSelf = 0x36,
    NotPreoccupied = 0x37,
    NotMyNpc = 0x38,
    NotCheckedSecondPass = 0x39,
    ZoneBanned = 0x3A,
    InvalidGradeEnchantSupportItem = 0x3B,
    CheckCharacterPStatMin = 0x3C,
    CheckCharacterPStatMax = 0x3D,
    ItemSecured = 0x3E,
    InvalidAccountAttribute = 0x3F,
    FestivalZone = 0x40,
    AlreadyOtherPlayerBound = 0x41,
    MateDead = 0x42,
    CannotUnsummonUnderStunSleepRoot = 0x43,
    LackHighAbilityResource = 0x44,
    LackSourceItemSet = 0x45,
    LackActability = 0x46,
    UrkStart = 0x46, // Start offset for UnitReqsKindType
#pragma warning disable CA1069 // Enums values should not be duplicated
    UrkLevel = 0x47,
#pragma warning restore CA1069 // Enums values should not be duplicated
    UrkAbility = 0x48,
    UrkRace = 0x49,
    UrkGender = 0x4A,
    UrkEquipSlot = 0x4B,
    UrkEquipItem = 0x4C,
    UrkOwnItem = 0x4D,
    UrkTrainedSkill = 0x4E,
    UrkCombat = 0x4F,
    UrkStealth = 0x50,
    UrkHealth = 0x51,
    UrkBuff = 0x52,
    UrkTargetBuff = 0x53,
    UrkTargetCombat = 0x54,
    UrkCanLearnCraft = 0x55,
    UrkDoodadRange = 0x56,
    UrkEquipShield = 0x57,
    UrkNobuff = 0x58,
    UrkTargetBuffTag = 0x59,
    UrkCorpseRange = 0x5A,
    UrkEquipWeaponType = 0x5B,
    UrkTargetHealthLessThan = 0x5C,
    UrkTargetNpc = 0x5D,
    UrkTargetDoodad = 0x5E,
    UrkEquipRanged = 0x5F,
    UrkNoBuffTag = 0x60,
    UrkCompleteQuestContext = 0x61,
    UrkProgressQuestContext = 0x62,
    UrkReadyQuestContext = 0x63,
    UrkTargetNpcGroup = 0x64,
    UrkAreaSphere = 0x65,
    UrkExceptCompleteQuestContext = 0x66,
    UrkPrecompleteQuestContext = 0x67,
    UrkTargetOwnerType = 0x68,
    UrkNotUnderWater = 0x69,
    UrkFactionMatch = 0x6A,
    UrkTod = 0x6B,
    UrkMotherFaction = 0x6C,
    UrkActabilityPoint = 0x6D,
    UrkCrimePoint = 0x6E,
    UrkHonorPoint = 0x6F,
    UrkLivingPoint = 0x70,
    UrkCrimeRecord = 0x71,
    UrkJuryPoint = 0x72,
    UrkSourceOwnerType = 0x73,
    UrkAppellation = 0x74,
    UrkInZone = 0x75,
    UrkOutZone = 0x76,
    UrkDominionOwner = 0x77,
    UrkVerdictOnly = 0x78,
    UrkFactionMatchOnly = 0x79,
    UrkMotherFactionOnly = 0x7A,
    UrkNationOwner = 0x7B,
    UrkFactionMatchOnlyNot = 0x7C,
    UrkMotherFactionOnlyNot = 0x7D,
    UrkNationMember = 0x7E,
    UrkNationMemberNot = 0x7F,
    UrkNationOwnerAtPos = 0x80,
    UrkDominionOwnerAtPos = 0x81,
    UrkHousing = 0x82,
    UrkHealthMargin = 0x83,
    UrkManaMargin = 0x84,
    UrkLaborPowerMargin = 0x85,
    UrkNotOnMovingPhysicalVehicle = 0x86,
    UrkMaxLevel = 0x87,
    UrkExpeditionOwner = 0x88,
    UrkExpeditionMember = 0x89,
    UrkExceptProgressQuestContext = 0x8A,
    UrkExceptReadyQuestContext = 0x8B,
    UrkOwnItemNot = 0x8C,
    UrkLessActabilityPoint = 0x8D,
    UrkOwnQuestItemGroup = 0x8E,
}

// ReSharper disable InconsistentNaming
/// <summary>
/// Internally used enum for generating SkillResults, do not pass directly to the client
/// </summary>
public enum SkillResultKeys
{
    // NOTE: do not edit the formatting or case of these enums
    ok,
    skill_failure,
    skill_source_died,
    skill_source_alive,
    skill_target_died,
    skill_target_destroyed,
    skill_target_alive,
    skill_on_casting,
    skill_cooldown_time,
    skill_no_target,
    skill_lack_health,
    skill_lack_mana,
    skill_obstacle,
    skill_outof_height,
    skill_too_close_range,
    skill_too_far_range,
    skill_outof_angle,
    skill_cannot_cast_in_combat,
    skill_cannot_cast_while_moving,
    skill_cannot_cast_in_stun,
    skill_cannot_cast_while_walking,
    skill_cannot_cast_in_swimming,
    skill_blank_minded,
    skill_silence,
    skill_crippled,
    skill_cannot_cast_in_channeling,
    skill_cannot_cast_in_prison,
    skill_need_stealth,
    skill_need_nocombat_target,
    skill_target_immune,
    skill_invalid_skill,
    skill_inactive_ability,
    skill_not_enough_ability_level,
    skill_invalid_source,
    skill_invalid_target,
    skill_invalid_location,
    skill_need_reagent,
    skill_item_locked,
    skill_need_money,
    skill_need_labor_power,
    skill_source_is_hanging,
    skill_source_is_riding,
    skill_higher_buff,
    skill_not_pvp_area,
    skill_not_now,
    skill_no_perm,
    skill_bag_full,
    skill_protected_faction,
    skill_protected_level,
    skill_unit_reqs_or_fail,
    backpack_occupied,
    skill_obstacle_for_spawn_doodad,
    skill_cannot_spawn_doodad_in_house,
    skill_cannot_use_for_self,
    skill_not_preoccupied,
    skill_not_my_npc,
    skill_not_checked_second_pass,
    // SKILL_CANNOT_USE_HERE,
    skill_invalid_grade_enchant_support_item,
    skill_check_character_p_stat_min,
    skill_check_character_p_stat_max,
    skill_invalid_account_attribute,
    skill_urk_level,
    skill_urk_ability,
    skill_urk_race,
    skill_urk_gender,
    skill_urk_equip_slot,
    skill_urk_equip_item,
    skill_urk_own_item,
    skill_urk_trained_skill,
    skill_urk_combat,
    skill_urk_stealth,
    skill_urk_health,
    skill_urk_buff,
    skill_urk_target_buff,
    skill_urk_target_combat,
    skill_urk_can_learn_craft,
    skill_urk_doodad_range,
    skill_urk_equip_shield,
    skill_urk_nobuff,
    skill_urk_target_buff_tag,
    skill_urk_corpse_range,
    skill_urk_equip_weapon_type,
    skill_urk_target_health_less_than,
    skill_urk_target_npc,
    skill_urk_target_doodad,
    skill_urk_equip_ranged,
    skill_urk_no_buff_tag,
    skill_urk_complete_quest_context,
    skill_urk_progress_quest_context,
    skill_urk_ready_quest_context,
    skill_urk_target_npc_group,
    skill_urk_area_sphere,
    skill_urk_precomplete_quest_context,
    skill_urk_target_owner_type,
    skill_urk_not_under_water,
    skill_urk_faction_match,
    skill_urk_tod,
    skill_urk_mother_faction,
    skill_urk_actability_point,
    skill_urk_honor_point,
    skill_urk_living_point,
    skill_urk_in_zone,
    skill_urk_out_zone,
    skill_urk_dominion_owner,
    skill_urk_verdict_only,
    skill_urk_faction_match_only,
    skill_urk_mother_faction_only,
    skill_urk_faction_match_only_not,
    skill_urk_mother_faction_only_not,
    skill_urk_nation_member,
    skill_urk_nation_member_not,
    skill_urk_housing,
    skill_urk_mana_margin,
    skill_urk_labor_power_margin,
    skill_urk_unknown,
    skill_urk_max_level,
}
// ReSharper restore InconsistentNaming

/// <summary>
/// Helper class to generate skill result error messages
/// </summary>
public static class SkillResultHelper
{
    
    public static SkillResult SkillResultErrorKeyToId(SkillResultKeys key)
    {
        // if (ClientVersion == r208022)
        return SkillResultErrorKeyToIdFor_r208022(key.ToString());
    }
    
    /// <summary>
    /// Lookup the SkillResult for Version 1.2 r208022
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private static SkillResult SkillResultErrorKeyToIdFor_r208022(string key)
    {
        switch (key)
        {
            case "": return SkillResult.Success;
            case "skill_success": return SkillResult.Success;
            case "skill_failure": return (SkillResult)1; //	Can't use this.
            case "skill_source_died": return (SkillResult)2; //	Can't be used while dead.
            case "skill_source_alive": return (SkillResult)3; //	Can only be used while dead.
            case "skill_target_died": return (SkillResult)4; //	Can't be used on a dead target.
            case "skill_target_destroyed": return (SkillResult)5; //	Target is already destroyed.
            case "skill_target_alive": return (SkillResult)6; //	Can't be used on a living target.
            case "skill_on_casting": return (SkillResult)7; //	Already performing an action.
            case "skill_cooldown_time": return (SkillResult)8; //	Can't be used right now.
            case "skill_no_target": return (SkillResult)9; //	Select a target.
            case "skill_lack_health": return (SkillResult)10; //	Insufficient health to use this.
            case "skill_lack_mana": return (SkillResult)11; //	Insufficient mana to use this.
            case "skill_obstacle": return (SkillResult)12; //	No line of sight.
            case "skill_outof_height": return (SkillResult)13; //	Target is on a different elevation.
            case "skill_too_close_range": return (SkillResult)14; //	Target is too close.
            case "skill_too_far_range": return (SkillResult)15; //	Target is too far.
            case "skill_outof_angle": return (SkillResult)16; //	Invalid target direction.
            case "skill_cannot_cast_in_combat": return (SkillResult)17; //	Can't be used in combat.
            case "skill_cannot_cast_while_moving": return (SkillResult)18; //	Can't be used while moving.
            case "skill_cannot_cast_in_stun": return (SkillResult)19; //	Can't be used while stunned.
            case "skill_cannot_cast_while_walking": return (SkillResult)20; //	Can't use while walking.
            case "skill_cannot_cast_in_swimming": return (SkillResult)21; //	Can't use this while swimming.
            case "skill_blank_minded": return (SkillResult)22; //	Can't use in ($1) unknown.
            case "skill_silence": return (SkillResult)23; //	Can't use magic skills while silenced.
            case "skill_crippled": return (SkillResult)24; //	Can't use physical skills while restrained.
            case "skill_cannot_cast_in_channeling": return (SkillResult)25; //	Can't use while busy.
            case "skill_cannot_cast_in_prison": return (SkillResult)26; //	Stay away from trouble while imprisoned.
            case "skill_need_stealth": return (SkillResult)27; //	Can only use while hidden.
            case "skill_need_nocombat_target": return (SkillResult)28; //	Target is in combat.
            case "skill_target_immune": return (SkillResult)29; //	Target is immune.
            case "skill_invalid_skill": return (SkillResult)30; //	Can't use this skill.
            case "skill_inactive_ability": return (SkillResult)31; //	Can't use this ability.
            case "skill_not_enough_ability_level": return (SkillResult)32; //	Insufficient skill level.
            case "skill_invalid_source": return (SkillResult)33; //	Can't be used in this state.
            case "skill_invalid_target": return (SkillResult)34; //	Invalid target.
            case "skill_invalid_location": return (SkillResult)35; //	Can't be used here.
            case "skill_need_reagent": return (SkillResult)36; //	Not enough ($1) unknown.
            case "skill_item_locked": return (SkillResult)37; //	Can't use this item.
            case "skill_need_money": return (SkillResult)38; //	Insufficient coins.
            case "skill_need_labor_power": return (SkillResult)39; //	Insufficient Labor Points.
            case "skill_source_is_hanging": return (SkillResult)40; //	Can't be used while airborne.
            case "skill_source_is_riding": return (SkillResult)41; //	Can't be used while riding.
            case "skill_higher_buff": return (SkillResult)42; //	Can't be used while a stronger effect is active.
            case "skill_not_pvp_area": return (SkillResult)43; //	PvP is not allowed in sanctuary zones.
            case "skill_not_now": return (SkillResult)44; //	Can't be used right now.
            case "skill_no_perm": return (SkillResult)45; //	You don't have permission.
            case "skill_bag_full": return (SkillResult)46; //	Your bag is full.
            case "skill_protected_faction": return (SkillResult)47; //	Can't instigate a raid against the ($1) unknown faction in this area.
            case "skill_protected_level": return (SkillResult)48; //	Can't start battles with characters Lv10 and below in protected zones.
            case "skill_unit_reqs_or_fail": return (SkillResult)49; //	Fails the requirements.
            // case "": return (SkillResult)50; //	unknown
            case "backpack_occupied": return (SkillResult)51; //	Already carrying a pack.
            case "skill_obstacle_for_spawn_doodad": return (SkillResult)52; //	Blocked by an obstacle.
            case "skill_cannot_spawn_doodad_in_house": return (SkillResult)53; //	Can't place that here.
            case "skill_cannot_use_for_self": return (SkillResult)54; //	Can't use on yourself.
            case "skill_not_preoccupied": return (SkillResult)55; //	Can only be used on selected targets.
            case "skill_not_my_npc": return (SkillResult)56; //	You don't have permission.
            case "skill_not_checked_second_pass": return (SkillResult)57; //	Failed to pass the second password.
            case "SKILL_CANNOT_USE_HERE": return (SkillResult)58; //	Can't use this skill in this location.
            case "skill_invalid_grade_enchant_support_item": return (SkillResult)59; //	Can't use a Regrade Charm.
            case "skill_check_character_p_stat_min": return (SkillResult)60; //	You can downgrade this stat to ($1) unknown.
            case "skill_check_character_p_stat_max": return (SkillResult)61; //	You can upgrade this stat to ($1) unknown.
            case "skill_item_secured": return (SkillResult)62; //	89 ?? skill_item_secured
            case "skill_invalid_account_attribute": return (SkillResult)63; //	Your account doesn't have the required permissions.
            case "skill_urk_level": return (SkillResult)64; //	Your level is too low.
            case "skill_urk_ability": return (SkillResult)65; //	Your stats are too low.
            case "skill_urk_race": return (SkillResult)66; //	Does not apply to this race.
            case "skill_urk_gender": return (SkillResult)67; //	Does not apply to this gender.
            case "skill_urk_equip_slot": return (SkillResult)68; //	Must be equipped with the proper gear.
            case "skill_urk_equip_item": return (SkillResult)69; //	Must be equipped with an item.
            case "skill_urk_own_item": return (SkillResult)70; //	You need ($1) unknown(|r.)
            case "skill_urk_trained_skill": return (SkillResult)71; //	You haven't learned this skill yet.
            case "skill_urk_combat": return (SkillResult)72; //	Can't be used in combat.
            case "skill_urk_stealth": return (SkillResult)73; //	Stealth status does not meet the requirements.
            case "skill_urk_health": return (SkillResult)74; //	Health does not meet the requirements.
            case "skill_urk_buff": return (SkillResult)75; //	Must be ($1) unknown.
            case "skill_urk_target_buff": return (SkillResult)76; //	Target must be ($1) unknown.
            case "skill_urk_target_combat": return (SkillResult)77; //	Target's combat status does not meet the requirements.
            case "skill_urk_can_learn_craft": return (SkillResult)78; //	You already learned this crafting skill.
            case "skill_urk_doodad_range": return (SkillResult)79; //	$1 is not in your immediate surroundings.
            case "skill_urk_equip_shield": return (SkillResult)80; //	Must be equipped with a shield.
            case "skill_urk_nobuff": return (SkillResult)81; //	Must not be under the effect of ($1) unknown.
            case "skill_urk_target_buff_tag": return (SkillResult)82; //	Target must be ($1) unknown.
            case "skill_urk_corpse_range": return (SkillResult)83; //	No corpses nearby.
            case "skill_urk_equip_weapon_type": return (SkillResult)84; //	Must be equipped with the correct weapon.
            case "skill_urk_target_health_less_than": return (SkillResult)85; //	Target's health must be low.
            case "skill_urk_target_npc": return (SkillResult)86; //	Can only be used on ($1) unknown.
            case "skill_urk_target_doodad": return (SkillResult)87; //	Invalid object.
            case "skill_urk_equip_ranged": return (SkillResult)88; //	Must be equipped with a ranged weapon.
            case "skill_urk_no_buff_tag": return (SkillResult)89; //	Can't do this now.
            case "skill_urk_complete_quest_context": return (SkillResult)90; //	Quest: $1 must be completed.
            case "skill_urk_progress_quest_context": return (SkillResult)91; //	Quest: $1 must be in-progress.
            case "skill_urk_ready_quest_context": return (SkillResult)92; //	Quest: $1 must be completed.
            case "skill_urk_target_npc_group": return (SkillResult)93; //	Invalid target.
            case "skill_urk_area_sphere": return (SkillResult)94; //	Can't be used here.
            case "skill_urk_except_complete_quest_context": return (SkillResult)95; //	89 ?? Skill_urk_except_complete_quest_context
            case "skill_urk_precomplete_quest_context": return (SkillResult)96; //	Quest: $1 must be in-progress.
            case "skill_urk_target_owner_type": return (SkillResult)97; //	Invalid target.
            case "skill_urk_not_under_water": return (SkillResult)98; //	Can't use underwater.
            case "skill_urk_faction_match": return (SkillResult)99; //	You are not a member of the $1 faction.
            case "skill_urk_tod": return (SkillResult)100; //	Can't be used at this time.
            case "skill_urk_mother_faction": return (SkillResult)101; //	Your faction can't use this.
            case "skill_urk_actability_point": return (SkillResult)102; //	Insufficient $1 proficiency.
            case "skill_urk_crime_point": return (SkillResult)103; //	89 ?? Skill_urk_crime_point
            case "skill_urk_honor_point": return (SkillResult)104; //	You don't meet the Honor Point requirements.
            case "skill_urk_living_point": return (SkillResult)105; //	You don't meet the Vocation Badge requirements.
            case "skill_urk_crime_record": return (SkillResult)106; //	89 ?? Skill_urk_crime_record
            case "skill_urk_jury_point": return (SkillResult)107; //	89 ?? Skill_urk_jury_point
            case "skill_urk_source_owner_type": return (SkillResult)108; //	89 ?? Skill_urk_source_owner_type
            case "skill_urk_appelation": return (SkillResult)109; //	89 ?? Skill_urk_appelation
            case "skill_urk_in_zone": return (SkillResult)110; //	Can only be used in $1.
            case "skill_urk_out_zone": return (SkillResult)111; //	Can't be used in $1.
            case "skill_urk_dominion_owner": return (SkillResult)112; //	Only Lords can do this.
            case "skill_urk_verdict_only": return (SkillResult)113; //	Your jury privileges have been revoked. You can no longer serve on juries.
            case "skill_urk_faction_match_only": return (SkillResult)114; //	You are not a member of the $1 faction.
            case "skill_urk_mother_faction_only": return (SkillResult)115; //	Your faction can't use this.
            case "skill_urk_nation_owner": return (SkillResult)116; //	89 ?? Skill_urk_nation_owner
            case "skill_urk_faction_match_only_not": return (SkillResult)117; //	$1+ HP must be drained first. // This translation seems wrong
            case "skill_urk_mother_faction_only_not": return (SkillResult)118; //	The $1 sub faction can't do this.
            case "skill_urk_nation_member": return (SkillResult)119; //	You must be in a nation.
            case "skill_urk_nation_member_not": return (SkillResult)120; //	You can't be in a nation to do this.
            case "skill_urk_nation_owner_at_pos": return (SkillResult)121; //	89 ?? Skill_urk_nation_owner_at_pos
            case "skill_urk_dominion_owner_at_pos": return (SkillResult)122; //	89 ?? Skill_urk_dominion_owner_at_pos
            case "skill_urk_housing": return (SkillResult)123; //	You do not have $1.
            case "skill_urk_health_margin": return (SkillResult)124; //	89 ?? Skill_urk_health_margin
            case "skill_urk_mana_margin": return (SkillResult)125; //	$1+ MP must be drained first.
            case "skill_urk_labor_power_margin": return (SkillResult)126; //	$1+ Labor must be drained first.
            case "skill_urk_unknown": return (SkillResult)127; //	Can't use this.
            case "skill_urk_max_level": return (SkillResult)128; //	Your level is too high.
            case "skill_urk_expedition_owner": return (SkillResult)129; //	89 ?? Skill_urk_expedition_owner
            case "skill_urk_expedition_member": return (SkillResult)130; //	89 ?? Skill_urk_expedition_member
            // case "skill_urk_progress_quest_context": return (SkillResult)131; //	89 ?? Skill_urk_progress_quest_context
            // case "skill_urk_ready_quest_context": return (SkillResult)132; //	89 ?? Skill_urk_ready_quest_context
           default: return SkillResult.Failure;
        }
    }
}
