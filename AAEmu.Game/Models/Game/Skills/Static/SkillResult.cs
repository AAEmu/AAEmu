namespace AAEmu.Game.Models.Game.Skills.Static;

/// <summary>
/// Extracted enum of skill results, might not be correct
/// </summary>
public enum SkillResult : byte
{
    // updated to version 5.0.7.0
    Success = 0,
    Failure = 1,
    SourceDied = 2,
    SourceAlive = 3,
    TargetDied = 4,
    TargetDestroyed = 5,
    TargetAlive = 6,
    OnCasting = 7,
    CooldownTime = 8,
    NoTarget = 9,
    LackHealth = 10,
    LackMana = 11,
    Obstacle = 12,
    OutofHeight = 13,
    TooCloseRange = 14,
    TooFarRange = 15,
    OutofAngle = 16,
    CannotCastInCombat = 17,
    CannotCastWhileMoving = 18,
    CannotCastInStun = 19,
    CannotCastWhileWalking = 20,
    CannotCastInSwimming = 21,
    BlankMinded = 22,
    Silence = 23,
    Crippled = 24,
    CannotCastInChanneling = 25,
    CannotCastInPrison = 26,
    NeedStealth = 27,
    NeedNocombatTarget = 28,
    TargetImmune = 29,
    InvalidSkill = 30,
    InactiveAbility = 31,
    NotEnoughAbilityLevel = 32,
    InvalidSource = 33,
    InvalidTarget = 34,
    InvalidLocation = 35,
    NeedReagent = 36,
    ItemLocked = 37,
    NeedMoney = 38,
    NeedLaborPower = 39,
    SourceIsHanging = 40,
    SourceIsRiding = 41,
    HigherBuff = 42,
    NotPvpArea = 43,
    NotNow = 44,
    NoPerm = 45,
    BagFull = 46,
    ProtectedFaction = 47,
    ProtectedLevel = 48,
    UnitReqsOrFail = 49,
    SkillReqFail = 50,
    BackpackOccupied = 51,
    ObstacleForSpawnDoodad = 52,
    CannotSpawnDoodadInHouse = 53,
    CannotUseForSelf = 54,
    NotPreoccupied = 55,
    NotMyNpc = 56,
    NotCheckedSecondPass = 57,
    ZoneBanned = 58,
    InvalidGradeEnchantSupportItem = 59,
    CheckCharacterPStatMin = 60,
    CheckCharacterPStatMax = 61,
    ItemSecured = 62,
    InvalidAccountAttribute = 63,
    FestivalZone = 64,
    AlreadyOtherPlayerBound = 65,
    CannotUnsummonUnderStunSleepRoot = 67,
    LackHighAbilityResource = 68,
    LackSourceItemSet = 69,
    LackActability = 70,
    OnlyDuringSwimming = 71,
    UrkLevel = 72, // Start offset for UnitReqsKindType
    UrkAbility = 73,
    //UrkAbility = 164,
    UrkRace = 74,
    UrkGender = 75,
    UrkEquipSlot = 76,
    UrkEquipItem = 77,
    UrkOwnItem = 78,
    UrkTrainedSkill = 79,
    UrkCombat = 80,
    UrkStealth = 81,
    UrkHealth = 82,
    UrkBuff = 83,
    UrkTargetBuff = 84,
    UrkTargetCombat = 85,
    UrkCanLearnCraft = 86,
    UrkDoodadRange = 87,
    UrkEquipShield = 88,
    UrkNobuff = 89,
    UrkTargetBuffTag = 90,
    UrkCorpseRange = 91,
    UrkEquipWeaponType = 92,
    UrkTargetHealthLessThan = 93,
    UrkTargetNpc = 94,
    UrkTargetDoodad = 95,
    UrkEquipRanged = 96,
    UrkNoBuffTag = 97,
    UrkCompleteQuestContext = 98,
    UrkProgressQuestContext = 99,
    UrkReadyQuestContext = 100,
    UrkTargetNpcGroup = 101,
    UrkAreaSphere = 102,
    UrkExceptCompleteQuestContext = 103,
    UrkPrecompleteQuestContext = 104,
    UrkTargetOwnerType = 105,
    UrkNotUnderWater = 106,
    UrkFactionMatch = 107,
    UrkTod = 108,
    UrkMotherFaction = 109,
    UrkActabilityPoint = 110,
    UrkCrimePoint = 111,
    UrkHonorPoint = 112,
    UrkLivingPoint = 113,
    UrkCrimeRecord = 114,
    UrkJuryPoint = 115,
    UrkSourceOwnerType = 116,
    UrkAppellation = 117,
    UrkInZone = 118,
    UrkOutZone = 119,
    UrkDominionOwner = 120,
    UrkVerdictOnly = 121,
    UrkFactionMatchOnly = 122,
    UrkMotherFactionOnly = 123,
    UrkNationOwner = 124,
    UrkFactionMatchOnlyNot = 125,
    UrkMotherFactionOnlyNot = 126,
    UrkNationMember = 127,
    UrkNationMemberNot = 128,
    UrkNationOwnerAtPos = 129,
    UrkDominionOwnerAtPos = 130,
    UrkHousing = 131,
    UrkHealthMargin = 132,
    UrkManaMargin = 133,
    UrkLaborPowerMargin = 134,
    UrkMaxLevel = 136,
    UrkExpeditionOwner = 137,
    UrkExpeditionMember = 138,
    UrkExceptProgressQuestContext = 139,
    UrkExceptReadyQuestContext = 140,
    UrkOwnItemNot = 141,
    UrkLessActabilityPoint = 142,
    UrkOwnQuestItemGroup = 143,
    UrkLeadershipTotal = 144,
    UrkLeadershipCurrent = 145,
    UrkHero = 146,
    UrkOwnItemCount = 149,
    UrkHouse = 151,
    UrkHouseOnly = 152,
    UrkDecoLimitExpanded = 153,
    UrkNotExpandable = 154,
    UrkDoodadTargetFriendly = 155,
    UrkDoodadTargetHostile = 156,
    UrkDominionExpeditionMemberNot = 157,
    UrkDominionMemberNot = 158,
    UrkCannotUseBuildingHouse = 159,
    UrkTargetNobuffTag = 160,
    //UrkAbility = 164,
    UrkTargetManaLessThan = 165,
    UrkTargetManaMoreThan = 166,
    UrkTargetHealthMoreThan = 167,
    UrkBuffTag = 168,
    UrkFamilyRole = 169,
    UrkLaborPowerMarginLocal = 170,
    UrkHeirLevel = 171,
    UrkInZoneGroup = 172,
    UrkUnderWater = 174,
    UrkOwnAppellation = 175,
    UrkEquipAppellation = 176,
    UrkFullRechargedLaborPower = 178
//    default:
//      result = "URK_UNKNOWN";
}

// ReSharper disable InconsistentNaming
/// <summary>
/// Internally used enum for generating SkillResults, do not pass directly to the client
/// </summary>
public enum SkillResultKeys
{
    // updated to version 5.0.7.0
    // NOTE: do not edit the formatting or case of these enums
    ok,
    skill_success,
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
    skill_skill_req_fail,
    skill_backpack_occupied,
    skill_obstacle_for_spawn_doodad,
    skill_cannot_spawn_doodad_in_house,
    skill_cannot_use_for_self,
    skill_not_preoccupied,
    skill_not_my_npc,
    skill_not_checked_second_pass,
    skill_zone_banned, // SKILL_CANNOT_USE_HERE
    skill_invalid_grade_enchant_support_item,
    skill_check_character_p_stat_min,
    skill_check_character_p_stat_max,
    skill_item_secured,
    skill_invalid_account_attribute,
    skill_festival_zone,
    skill_already_other_player_bound,
    skill_cannot_unsummon_under_stun_sleep_root,
    skill_lack_high_ability_resource,
    skill_lack_source_item_set,
    skill_lack_actability,
    skill_only_during_swimming,
    skill_urk_level,
    skill_urk_ability,
    //skill_urk_ability,
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
    skill_urk_except_complete_quest_context,
    skill_urk_precomplete_quest_context,
    skill_urk_target_owner_type,
    skill_urk_not_under_water,
    skill_urk_faction_match,
    skill_urk_tod,
    skill_urk_mother_faction,
    skill_urk_actability_point,
    skill_urk_crime_point,
    skill_urk_honor_point,
    skill_urk_living_point,
    skill_urk_crime_record,
    skill_urk_jury_point,
    skill_urk_source_owner_type,
    skill_urk_appellation,
    skill_urk_in_zone,
    skill_urk_out_zone,
    skill_urk_dominion_owner,
    skill_urk_verdict_only,
    skill_urk_faction_match_only,
    skill_urk_mother_faction_only,
    skill_urk_nation_owner,
    skill_urk_faction_match_only_not,
    skill_urk_mother_faction_only_not,
    skill_urk_nation_member,
    skill_urk_nation_member_not,
    skill_urk_nation_owner_at_pos,
    skill_urk_dominion_owner_at_pos,
    skill_urk_housing,
    skill_urk_health_margin,
    skill_urk_mana_margin,
    skill_urk_labor_power_margin,
    skill_urk_max_level,
    skill_urk_expedition_owner,
    skill_urk_expedition_member,
    skill_urk_except_progress_quest_context,
    skill_urk_except_ready_quest_context,
    skill_urk_own_item_not,
    skill_urk_less_actability_point,
    skill_urk_own_quest_item_group,
    skill_urk_leadership_total,
    skill_urk_leadership_current,
    skill_urk_hero,
    skill_urk_own_item_count,
    skill_urk_house,
    skill_urk_house_only,
    skill_urk_deco_limit_expanded,
    skill_urk_not_expandable,
    skill_urk_doodad_target_friendly,
    skill_urk_doodad_target_hostile,
    skill_urk_dominion_expedition_member_not,
    skill_urk_dominion_member_not,
    skill_urk_cannot_use_building_house,
    skill_urk_target_nobuff_tag,
    //skill_urk_ability = 164,
    skill_urk_target_mana_less_than,
    skill_urk_target_mana_more_than,
    skill_urk_target_health_more_than,
    skill_urk_buff_tag,
    skill_urk_family_role,
    skill_urk_labor_power_margin_local,
    skill_urk_heir_level,
    skill_urk_in_zone_group,
    skill_urk_under_water,
    skill_urk_own_appellation,
    skill_urk_equip_appellation,
    skill_urk_full_recharged_labor_power,
    skill_urk_unknown
}// ReSharper restore InconsistentNaming

/// <summary>
/// Helper class to generate skill result error messages
/// </summary>
public static class SkillResultHelper
{
    public static SkillResult SkillResultErrorKeyToId(SkillResultKeys key)
    {
        // if (ClientVersion == r208022)
        // if (ClientVersion == r500700)
        return SkillResultErrorKeyToIdFor_r500700(key.ToString());
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
            case "skill_failure": return (SkillResult)1;                                // Can't use this.
            case "skill_source_died": return (SkillResult)2;                            // Can't be used while dead.
            case "skill_source_alive": return (SkillResult)3;                           // Can only be used while dead.
            case "skill_target_died": return (SkillResult)4;                            // Can't be used on a dead target.
            case "skill_target_destroyed": return (SkillResult)5;                       // Target is already destroyed.
            case "skill_target_alive": return (SkillResult)6;                           // Can't be used on a living target.
            case "skill_on_casting": return (SkillResult)7;                             // Already performing an action.
            case "skill_cooldown_time": return (SkillResult)8;                          // Can't be used right now.
            case "skill_no_target": return (SkillResult)9;                              // Select a target.
            case "skill_lack_health": return (SkillResult)10;                           // Insufficient health to use this.
            case "skill_lack_mana": return (SkillResult)11;                             // Insufficient mana to use this.
            case "skill_obstacle": return (SkillResult)12;                              // No line of sight.
            case "skill_outof_height": return (SkillResult)13;                          // Target is on a different elevation.
            case "skill_too_close_range": return (SkillResult)14;                       // Target is too close.
            case "skill_too_far_range": return (SkillResult)15;                         // Target is too far.
            case "skill_outof_angle": return (SkillResult)16;                           // Invalid target direction.
            case "skill_cannot_cast_in_combat": return (SkillResult)17;                 // Can't be used in combat.
            case "skill_cannot_cast_while_moving": return (SkillResult)18;              // Can't be used while moving.
            case "skill_cannot_cast_in_stun": return (SkillResult)19;                   // Can't be used while stunned.
            case "skill_cannot_cast_while_walking": return (SkillResult)20;             // Can't use while walking.
            case "skill_cannot_cast_in_swimming": return (SkillResult)21;               // Can't use this while swimming.
            case "skill_blank_minded": return (SkillResult)22;                          // Can't use in ($1) unknown.
            case "skill_silence": return (SkillResult)23;                               // Can't use magic skills while silenced.
            case "skill_crippled": return (SkillResult)24;                              // Can't use physical skills while restrained.
            case "skill_cannot_cast_in_channeling": return (SkillResult)25;             // Can't use while busy.
            case "skill_cannot_cast_in_prison": return (SkillResult)26;                 // Stay away from trouble while imprisoned.
            case "skill_need_stealth": return (SkillResult)27;                          // Can only use while hidden.
            case "skill_need_nocombat_target": return (SkillResult)28;                  // Target is in combat.
            case "skill_target_immune": return (SkillResult)29;                         // Target is immune.
            case "skill_invalid_skill": return (SkillResult)30;                         // Can't use this skill.
            case "skill_inactive_ability": return (SkillResult)31;                      // Can't use this ability.
            case "skill_not_enough_ability_level": return (SkillResult)32;              // Insufficient skill level.
            case "skill_invalid_source": return (SkillResult)33;                        // Can't be used in this state.
            case "skill_invalid_target": return (SkillResult)34;                        // Invalid target.
            case "skill_invalid_location": return (SkillResult)35;                      // Can't be used here.
            case "skill_need_reagent": return (SkillResult)36;                          // Not enough ($1) unknown.
            case "skill_item_locked": return (SkillResult)37;                           // Can't use this item.
            case "skill_need_money": return (SkillResult)38;                            // Insufficient coins.
            case "skill_need_labor_power": return (SkillResult)39;                      // Insufficient Labor Points.
            case "skill_source_is_hanging": return (SkillResult)40;                     // Can't be used while airborne.
            case "skill_source_is_riding": return (SkillResult)41;                      // Can't be used while riding.
            case "skill_higher_buff": return (SkillResult)42;                           // Can't be used while a stronger effect is active.
            case "skill_not_pvp_area": return (SkillResult)43;                          // PvP is not allowed in sanctuary zones.
            case "skill_not_now": return (SkillResult)44;                               // Can't be used right now.
            case "skill_no_perm": return (SkillResult)45;                               // You don't have permission.
            case "skill_bag_full": return (SkillResult)46;                              // Your bag is full.
            case "skill_protected_faction": return (SkillResult)47;                     // Can't instigate a raid against the ($1) unknown faction in this area.
            case "skill_protected_level": return (SkillResult)48;                       // Can't start battles with characters Lv10 and below in protected zones.
            case "skill_unit_reqs_or_fail": return (SkillResult)49;                     // Fails the requirements.
            // case "": return (SkillResult)50; //	unknown
            case "backpack_occupied": return (SkillResult)51;                           // Already carrying a pack.
            case "skill_obstacle_for_spawn_doodad": return (SkillResult)52;             // Blocked by an obstacle.
            case "skill_cannot_spawn_doodad_in_house": return (SkillResult)53;          // Can't place that here.
            case "skill_cannot_use_for_self": return (SkillResult)54;                   // Can't use on yourself.
            case "skill_not_preoccupied": return (SkillResult)55;                       // Can only be used on selected targets.
            case "skill_not_my_npc": return (SkillResult)56;                            // You don't have permission.
            case "skill_not_checked_second_pass": return (SkillResult)57;               // Failed to pass the second password.
            case "SKILL_CANNOT_USE_HERE": return (SkillResult)58;                       // Can't use this skill in this location.
            case "skill_invalid_grade_enchant_support_item": return (SkillResult)59;    //	Can't use a Regrade Charm.
            case "skill_check_character_p_stat_min": return (SkillResult)60;            // You can downgrade this stat to ($1) unknown.
            case "skill_check_character_p_stat_max": return (SkillResult)61;            // You can upgrade this stat to ($1) unknown.
            case "skill_item_secured": return (SkillResult)62;                          // 89 ?? skill_item_secured
            case "skill_invalid_account_attribute": return (SkillResult)63;             // Your account doesn't have the required permissions.
            case "skill_urk_level": return (SkillResult)64;                             // Your level is too low.
            case "skill_urk_ability": return (SkillResult)65;                           // Your stats are too low.
            case "skill_urk_race": return (SkillResult)66;                              // Does not apply to this race.
            case "skill_urk_gender": return (SkillResult)67;                            // Does not apply to this gender.
            case "skill_urk_equip_slot": return (SkillResult)68;                        // Must be equipped with the proper gear.
            case "skill_urk_equip_item": return (SkillResult)69;                        // Must be equipped with an item.
            case "skill_urk_own_item": return (SkillResult)70;                          // You need ($1) unknown(|r.)
            case "skill_urk_trained_skill": return (SkillResult)71;                     // You haven't learned this skill yet.
            case "skill_urk_combat": return (SkillResult)72;                            // Can't be used in combat.
            case "skill_urk_stealth": return (SkillResult)73;                           // Stealth status does not meet the requirements.
            case "skill_urk_health": return (SkillResult)74;                            // Health does not meet the requirements.
            case "skill_urk_buff": return (SkillResult)75;                              // Must be ($1) unknown.
            case "skill_urk_target_buff": return (SkillResult)76;                       // Target must be ($1) unknown.
            case "skill_urk_target_combat": return (SkillResult)77;                     // Target's combat status does not meet the requirements.
            case "skill_urk_can_learn_craft": return (SkillResult)78;                   // You already learned this crafting skill.
            case "skill_urk_doodad_range": return (SkillResult)79;                      // $1 is not in your immediate surroundings.
            case "skill_urk_equip_shield": return (SkillResult)80;                      // Must be equipped with a shield.
            case "skill_urk_nobuff": return (SkillResult)81;                            // Must not be under the effect of ($1) unknown.
            case "skill_urk_target_buff_tag": return (SkillResult)82;                   // Target must be ($1) unknown.
            case "skill_urk_corpse_range": return (SkillResult)83;                      // No corpses nearby.
            case "skill_urk_equip_weapon_type": return (SkillResult)84;                 // Must be equipped with the correct weapon.
            case "skill_urk_target_health_less_than": return (SkillResult)85;           // Target's health must be low.
            case "skill_urk_target_npc": return (SkillResult)86;                        //Can only be used on ($1) unknown.
            case "skill_urk_target_doodad": return (SkillResult)87;                     // Invalid object.
            case "skill_urk_equip_ranged": return (SkillResult)88;                      // Must be equipped with a ranged weapon.
            case "skill_urk_no_buff_tag": return (SkillResult)89;                       // Can't do this now.
            case "skill_urk_complete_quest_context": return (SkillResult)90;            // Quest: $1 must be completed.
            case "skill_urk_progress_quest_context": return (SkillResult)91;            // Quest: $1 must be in-progress.
            case "skill_urk_ready_quest_context": return (SkillResult)92;               // Quest: $1 must be completed.
            case "skill_urk_target_npc_group": return (SkillResult)93;                  // Invalid target.
            case "skill_urk_area_sphere": return (SkillResult)94;                       // Can't be used here.
            case "skill_urk_except_complete_quest_context": return (SkillResult)95;     // 89 ?? Skill_urk_except_complete_quest_context
            case "skill_urk_precomplete_quest_context": return (SkillResult)96;         // Quest: $1 must be in-progress.
            case "skill_urk_target_owner_type": return (SkillResult)97;                 // Invalid target.
            case "skill_urk_not_under_water": return (SkillResult)98;                   // Can't use underwater.
            case "skill_urk_faction_match": return (SkillResult)99;                     // You are not a member of the $1 faction.
            case "skill_urk_tod": return (SkillResult)100;                              // Can't be used at this time.
            case "skill_urk_mother_faction": return (SkillResult)101;                   // Your faction can't use this.
            case "skill_urk_actability_point": return (SkillResult)102;                 // Insufficient $1 proficiency.
            case "skill_urk_crime_point": return (SkillResult)103;                      // 89 ?? Skill_urk_crime_point
            case "skill_urk_honor_point": return (SkillResult)104;                      // You don't meet the Honor Point requirements.
            case "skill_urk_living_point": return (SkillResult)105;                     // You don't meet the Vocation Badge requirements.
            case "skill_urk_crime_record": return (SkillResult)106;                     // 89 ?? Skill_urk_crime_record
            case "skill_urk_jury_point": return (SkillResult)107;                       // 89 ?? Skill_urk_jury_point
            case "skill_urk_source_owner_type": return (SkillResult)108;                // 89 ?? Skill_urk_source_owner_type
            case "skill_urk_appelation": return (SkillResult)109;                       // 89 ?? Skill_urk_appelation
            case "skill_urk_in_zone": return (SkillResult)110;                          // Can only be used in $1.
            case "skill_urk_out_zone": return (SkillResult)111;                         // Can't be used in $1.
            case "skill_urk_dominion_owner": return (SkillResult)112;                   // Only Lords can do this.
            case "skill_urk_verdict_only": return (SkillResult)113;                     // Your jury privileges have been revoked. You can no longer serve on juries.
            case "skill_urk_faction_match_only": return (SkillResult)114;               // You are not a member of the $1 faction.
            case "skill_urk_mother_faction_only": return (SkillResult)115;              // Your faction can't use this.
            case "skill_urk_nation_owner": return (SkillResult)116;                     // 89 ?? Skill_urk_nation_owner
            case "skill_urk_faction_match_only_not": return (SkillResult)117;           // $1+ HP must be drained first. // This translation seems wrong
            case "skill_urk_mother_faction_only_not": return (SkillResult)118;          // The $1 sub faction can't do this.
            case "skill_urk_nation_member": return (SkillResult)119;                    // You must be in a nation.
            case "skill_urk_nation_member_not": return (SkillResult)120;                // You can't be in a nation to do this.
            case "skill_urk_nation_owner_at_pos": return (SkillResult)121;              // 89 ?? Skill_urk_nation_owner_at_pos
            case "skill_urk_dominion_owner_at_pos": return (SkillResult)122;            // 89 ?? Skill_urk_dominion_owner_at_pos
            case "skill_urk_housing": return (SkillResult)123;                          // You do not have $1.
            case "skill_urk_health_margin": return (SkillResult)124;                    // 89 ?? Skill_urk_health_margin
            case "skill_urk_mana_margin": return (SkillResult)125;                      // $1+ MP must be drained first.
            case "skill_urk_labor_power_margin": return (SkillResult)126;               // $1+ Labor must be drained first.
            case "skill_urk_unknown": return (SkillResult)127;                          // Can't use this.
            case "skill_urk_max_level": return (SkillResult)128;                        // Your level is too high.
            case "skill_urk_expedition_owner": return (SkillResult)129;                 // 89 ?? Skill_urk_expedition_owner
            case "skill_urk_expedition_member": return (SkillResult)130;                // 89 ?? Skill_urk_expedition_member
            // case "skill_urk_progress_quest_context": return (SkillResult)131;        // 89 ?? Skill_urk_progress_quest_context
            // case "skill_urk_ready_quest_context": return (SkillResult)132;           // 89 ?? Skill_urk_ready_quest_context
           default: return SkillResult.Failure;
        }
    }

    private static SkillResult SkillResultErrorKeyToIdFor_r500700(string key)
    {
        switch (key)
        {
            // updated to version 5.0.7.0
            case "": return SkillResult.Success;
            case "skill_success": return SkillResult.Success;
            case "skill_failure": return (SkillResult)1;                                // Can't use this.
            case "skill_source_died": return (SkillResult)2;                            // Can't be used while dead.
            case "skill_source_alive": return (SkillResult)3;                           // Can only be used while dead.
            case "skill_target_died": return (SkillResult)4;                            // Can't be used on a dead target.
            case "skill_target_destroyed": return (SkillResult)5;                       // Target is already destroyed.
            case "skill_target_alive": return (SkillResult)6;                           // Can't be used on a living target.
            case "skill_on_casting": return (SkillResult)7;                             // Already performing an action.
            case "skill_cooldown_time": return (SkillResult)8;                          // Can't be used right now.
            case "skill_no_target": return (SkillResult)9;                              // Select a target.
            case "skill_lack_health": return (SkillResult)10;                           // Insufficient health to use this.
            case "skill_lack_mana": return (SkillResult)11;                             // Insufficient mana to use this.
            case "skill_obstacle": return (SkillResult)12;                              // No line of sight.
            case "skill_outof_height": return (SkillResult)13;                          // Target is on a different elevation.
            case "skill_too_close_range": return (SkillResult)14;                       // Target is too close.
            case "skill_too_far_range": return (SkillResult)15;                         // Target is too far.
            case "skill_outof_angle": return (SkillResult)16;                           // Invalid target direction.
            case "skill_cannot_cast_in_combat": return (SkillResult)17;                 // Can't be used in combat.
            case "skill_cannot_cast_while_moving": return (SkillResult)18;              // Can't be used while moving.
            case "skill_cannot_cast_in_stun": return (SkillResult)19;                   // Can't be used while stunned.
            case "skill_cannot_cast_while_walking": return (SkillResult)20;             // Can't use while walking.
            case "skill_cannot_cast_in_swimming": return (SkillResult)21;               // Can't use this while swimming.
            case "skill_blank_minded": return (SkillResult)22;                          // Can't use in $1
            case "skill_silence": return (SkillResult)23;                               // Can't use magic skills while silenced.
            case "skill_crippled": return (SkillResult)24;                              // Can't use physical skills while restrained.
            case "skill_cannot_cast_in_channeling": return (SkillResult)25;             // Can't use while busy.
            case "skill_cannot_cast_in_prison": return (SkillResult)26;                 // Stay away from trouble while imprisoned.
            case "skill_need_stealth": return (SkillResult)27;                          // Can only use while hidden.
            case "skill_need_nocombat_target": return (SkillResult)28;                  // Target is in combat.
            case "skill_target_immune": return (SkillResult)29;                         // Target is immune.
            case "skill_invalid_skill": return (SkillResult)30;                         // Can't use this skill.
            case "skill_inactive_ability": return (SkillResult)31;                      // Can't use this ability.
            case "skill_not_enough_ability_level": return (SkillResult)32;              // Insufficient skill level.
            case "skill_invalid_source": return (SkillResult)33;                        // Can't be used in this state.
            case "skill_invalid_target": return (SkillResult)34;                        // Invalid target.
            case "skill_invalid_location": return (SkillResult)35;                      // Can't be used here.
            case "skill_need_reagent": return (SkillResult)36;                          // Not enough $1
            case "skill_item_locked": return (SkillResult)37;                           // Can't use this item.
            case "skill_need_money": return (SkillResult)38;                            // Insufficient coins.
            case "skill_need_labor_power": return (SkillResult)39;                      // Insufficient Labor Points.
            case "skill_source_is_hanging": return (SkillResult)40;                     // Can't be used while airborne.
            case "skill_source_is_riding": return (SkillResult)41;                      // Can't be used while riding.
            case "skill_higher_buff": return (SkillResult)42;                           // Can't be used while a stronger effect is active.
            case "skill_not_pvp_area": return (SkillResult)43;                          // PvP is not allowed in sanctuary zones.
            case "skill_not_now": return (SkillResult)44;                               // Can't be used right now.
            case "skill_no_perm": return (SkillResult)45;                               // You don't have permission.
            case "skill_bag_full": return (SkillResult)46;                              // Your bag is full.
            case "skill_protected_faction": return (SkillResult)47;                     // Can't instigate a raid against the $1 faction in this area.
            case "skill_protected_level": return (SkillResult)48;                       // Can't start battles with characters Lv10 and below in protected zones.
            case "skill_unit_reqs_or_fail": return (SkillResult)49;                     // Fails the requirements.
            case "skill_skill_req_fail": return (SkillResult)50;                        // unknown
            case "skill_backpack_occupied": return (SkillResult)51;                     // Already carrying a pack.
            case "skill_obstacle_for_spawn_doodad": return (SkillResult)52;             // Blocked by an obstacle.
            case "skill_cannot_spawn_doodad_in_house": return (SkillResult)53;          // Can't place that here.
            case "skill_cannot_use_for_self": return (SkillResult)54;                   // Can't use on yourself.
            case "skill_not_preoccupied": return (SkillResult)55;                       // Can only be used on selected targets.
            case "skill_not_my_npc": return (SkillResult)56;                            // You don't have permission.
            case "skill_not_checked_second_pass": return (SkillResult)57;               // Failed to pass the second password.
            case "SKILL_CANNOT_USE_HERE": return (SkillResult)58;                       // Can't use this skill in this location.
            case "skill_invalid_grade_enchant_support_item": return (SkillResult)59;    // Can't use a Regrade Charm.
            case "skill_check_character_p_stat_min": return (SkillResult)60;            // You can downgrade this stat to $1
            case "skill_check_character_p_stat_max": return (SkillResult)61;            // You can upgrade this stat to $1
            case "skill_item_secured": return (SkillResult)62;                          // 89 ?? skill_item_secured
            case "skill_invalid_account_attribute": return (SkillResult)63;             // Your account doesn't have the required permissions.
            case "skill_festival_zone": return (SkillResult)64;                         // Can't attack during a festival.
            case "skill_already_other_player_bound": return (SkillResult)65;            // Someone else is already using this.
            //case "?": return (SkillResult)66; //
            case "skill_cannot_unsummon_under_stun_sleep_root": return (SkillResult)67; // Can't be desummoned during certain debuff effects.
            case "skill_lack_high_ability_resource": return (SkillResult)68;            // Can't be desummoned during certain debuff effects.
            case "skill_lack_source_item_set": return (SkillResult)69;                  // Not enough items.
            case "skill_lack_actability": return (SkillResult)70;                       // Not enough proficiency.
            case "skill_only_during_swimming": return (SkillResult)71;                  // Can be used only during swimming
            case "skill_urk_level": return (SkillResult)72;                             // Your level is too low.
            case "skill_urk_ability": return (SkillResult)73;                           // Your stats are too low.
            case "skill_urk_race": return (SkillResult)74;                              // Does not apply to this race.
            case "skill_urk_gender": return (SkillResult)75;                            // Does not apply to this gender.
            case "skill_urk_equip_slot": return (SkillResult)76;                        // Must be equipped with the proper gear.
            case "skill_urk_equip_item": return (SkillResult)77;                        // Must be equipped with an item.
            case "skill_urk_own_item": return (SkillResult)78;                          // You need $1
            case "skill_urk_trained_skill": return (SkillResult)79;                     // You haven't learned this skill yet.
            case "skill_urk_combat": return (SkillResult)80;                            // Can't be used in combat.
            case "skill_urk_stealth": return (SkillResult)81;                           // Stealth status does not meet the requirements.
            case "skill_urk_health": return (SkillResult)82;                            // Health does not meet the requirements.
            case "skill_urk_buff": return (SkillResult)83;                              // Must be $1
            case "skill_urk_target_buff": return (SkillResult)84;                       // Target must be $1
            case "skill_urk_target_combat": return (SkillResult)85;                     // Target's combat status does not meet the requirements.
            case "skill_urk_can_learn_craft": return (SkillResult)86;                   // You already learned this crafting skill.
            case "skill_urk_doodad_range": return (SkillResult)87;                      // $1 are not in your immediate surroundings.
            case "skill_urk_equip_shield": return (SkillResult)88;                      // Must be equipped with a shield.
            case "skill_urk_nobuff": return (SkillResult)89;                            // Must not be under the effect of $1
            case "skill_urk_target_buff_tag": return (SkillResult)90;                   // Target must be $1
            case "skill_urk_corpse_range": return (SkillResult)91;                      // No corpses nearby.
            case "skill_urk_equip_weapon_type": return (SkillResult)92;                 // Must be equipped with the correct weapon.
            case "skill_urk_target_health_less_than": return (SkillResult)93;           // Target's health must be low.
            case "skill_urk_target_npc": return (SkillResult)94;                        // Can only be used on $1
            case "skill_urk_target_doodad": return (SkillResult)95;                     // Invalid object.
            case "skill_urk_equip_ranged": return (SkillResult)96;                      // Must be equipped with a ranged weapon.
            case "skill_urk_no_buff_tag": return (SkillResult)97;                       // Can't do this now.
            case "skill_urk_complete_quest_context": return (SkillResult)98;            // Quest: $1 must be completed.
            case "skill_urk_progress_quest_context": return (SkillResult)99;            // Quest: $1 must be in-progress.
            case "skill_urk_ready_quest_context": return (SkillResult)100;              // Quest: $1 must be completed.
            case "skill_urk_target_npc_group": return (SkillResult)101;                 // Invalid target.
            case "skill_urk_area_sphere": return (SkillResult)102;                      // Can't be used here.
            case "skill_urk_except_complete_quest_context": return (SkillResult)103;    // Must not have completed the quest '$1.'
            case "skill_urk_precomplete_quest_context": return (SkillResult)104;        // Quest: $1 must be in-progress.
            case "skill_urk_target_owner_type": return (SkillResult)105;                // Invalid target.
            case "skill_urk_not_under_water": return (SkillResult)106;                  // Can't use underwater.
            case "skill_urk_faction_match": return (SkillResult)107;                    // You are not a member of the $1 faction.
            case "skill_urk_tod": return (SkillResult)108;                              // Can't be used at this time.
            case "skill_urk_mother_faction": return (SkillResult)109;                   // Your faction can't use this.
            case "skill_urk_actability_point": return (SkillResult)110;                 // Insufficient $1 proficiency.
            case "skill_urk_crime_point": return (SkillResult)111;                      // 89 ?? Skill_urk_crime_point
            case "skill_urk_honor_point": return (SkillResult)112;                      // Insufficient Honor.
            case "skill_urk_living_point": return (SkillResult)113;                     // Insufficient Vocation Badges.
            case "skill_urk_crime_record": return (SkillResult)114;                     // 89 ?? Skill_urk_crime_record
            case "skill_urk_jury_point": return (SkillResult)115;                       // 89 ?? Skill_urk_jury_point
            case "skill_urk_source_owner_type": return (SkillResult)116;                // 89 ?? Skill_urk_source_owner_type
            case "skill_urk_appelation": return (SkillResult)117;                       // 89 ?? Skill_urk_appelation
            case "skill_urk_in_zone": return (SkillResult)118;                          // Can only be used in $1.
            case "skill_urk_out_zone": return (SkillResult)119;                         // Can't be used in $1.
            case "skill_urk_dominion_owner": return (SkillResult)120;                   // Only Lords can do this.
            case "skill_urk_verdict_only": return (SkillResult)121;                     // Your jury privileges have been revoked. You can no longer serve on juries.
            case "skill_urk_faction_match_only": return (SkillResult)122;               // You are not a member of the $1 faction.
            case "skill_urk_mother_faction_only": return (SkillResult)123;              // Your faction can't use this.
            case "skill_urk_nation_owner": return (SkillResult)124;                     // 89 ?? Skill_urk_nation_owner
            case "skill_urk_faction_match_only_not": return (SkillResult)125;           // The $1 faction can't use this.
            case "skill_urk_mother_faction_only_not": return (SkillResult)126;          // The $1 faction can't use this.
            case "skill_urk_nation_member": return (SkillResult)127;                    // You must be in a nation.
            case "skill_urk_nation_member_not": return (SkillResult)128;                // You can't use this while in a nation.
            case "skill_urk_nation_owner_at_pos": return (SkillResult)129;              // 89 ?? Skill_urk_nation_owner_at_pos
            case "skill_urk_dominion_owner_at_pos": return (SkillResult)130;            // 89 ?? Skill_urk_dominion_owner_at_pos
            case "skill_urk_housing": return (SkillResult)131;                          // You do not have $1.
            case "skill_urk_health_margin": return (SkillResult)132;                    // $1+ HP must be drained first.
            case "skill_urk_mana_margin": return (SkillResult)133;                      // $1+ MP must be drained first.
            case "skill_urk_labor_power_margin": return (SkillResult)134;               // $1+ Labor must be drained first.
            //case "skill_urk_unknown": return (SkillResult)135;                        // Can't use this. NotOnMovingPhysicalVehicle
            case "skill_urk_max_level": return (SkillResult)136;                        // Your level is too high.
            case "skill_urk_expedition_owner": return (SkillResult)137;                 // 89 ?? Skill_urk_expedition_owner
            case "skill_urk_expedition_member": return (SkillResult)138;                // 89 ?? Skill_urk_expedition_member
            case "skill_urk_except_progress_quest_context": return (SkillResult)139;    // Quest: $1 must not be in-progress.
            case "skill_urk_except_ready_quest_context": return (SkillResult)140;       // Quest: $1 must not be in-progress.
            case "skill_urk_own_item_not": return (SkillResult)141;                     // Can only carry one $1 at a time.
            case "skill_urk_less_actability_point": return (SkillResult)142;            // Can't be used, because your $1 proficiency is higher than $2.
            case "skill_urk_own_quest_item_group": return (SkillResult)143;             // Missing a required item.
            case "skill_urk_leadership_total": return (SkillResult)144;                 // 89 ?? skill_urk_leadership_total
            case "skill_urk_leadership_current": return (SkillResult)145;               // 89 ?? skill_urk_leadership_current
            case "skill_urk_hero": return (SkillResult)146;                             // 89 ?? skill_urk_hero
            // Can't use this.
            // Can't use this.
            case "skill_urk_own_item_count": return (SkillResult)149;                   // Not enough $1.
            // Can't use this.
            case "skill_urk_house": return (SkillResult)151;                            // Wrong target. Choose a different building.
            case "skill_urk_house_only": return (SkillResult)152;                       // Can only be used on buildings.
            case "skill_urk_deco_limit_expanded": return (SkillResult)153;              // Insufficient expansion tickets can't be used.
            case "skill_urk_not_expandable": return (SkillResult)154;                   // Decor capacity can't be increased for this structure.
            case "skill_urk_doodad_target_friendly": return (SkillResult)155;           // Applies to friendly targets.
            case "skill_urk_doodad_target_hostile": return (SkillResult)156;            // Applies to hostile targets.
            case "skill_urk_dominion_expedition_member_not": return (SkillResult)157;   // Not available to members of a ruling guild.
            case "skill_urk_dominion_member_not": return (SkillResult)158;              // Your guild or nation must not own any territory.
            case "skill_urk_cannot_use_building_house": return (SkillResult)159;        // Can't be used on a building under construction.
            case "skill_urk_target_nobuff_tag": return (SkillResult)160;                // Can't be used on items under $1.
            // Can't use this.
            // Can't use this.
            // Can't use this.
            //case "skill_urk_ability": return (SkillResult)164;                        // Your stats are too low.
            case "skill_urk_target_mana_less_than": return (SkillResult)165;            // Target must have low mana.
            case "skill_urk_target_mana_more_than": return (SkillResult)166;            // Target must have high mana.
            case "skill_urk_target_health_more_than": return (SkillResult)167;          // Target must have high health.
            case "skill_urk_buff_tag": return (SkillResult)168;                         // Must be $1.
            case "skill_urk_family_role": return (SkillResult)169;                      // 89 ?? skill_urk_family_role
            case "skill_urk_labor_power_margin_local": return (SkillResult)170;         // $1+ Labor must be drained first.
            case "skill_urk_heir_level": return (SkillResult)171;                       // Ancestral Level too low.
            case "skill_urk_in_zone_group": return (SkillResult)172;                    // Can only be used in $1.
            // Can't use this.
            case "skill_urk_under_water": return (SkillResult)174;                      // Can only be used in the water.
            case "skill_urk_own_appellation": return (SkillResult)175;                  // 89 ?? skill_urk_own_appellation
            case "skill_urk_equip_appellation": return (SkillResult)176;                // 89 ?? skill_urk_equip_appellation
            // Can't use this.
            case "skill_urk_full_recharged_labor_power": return (SkillResult)178;       // Maximum amount of daily labor restored.
           default: return SkillResult.Failure;
        }
    }
}
