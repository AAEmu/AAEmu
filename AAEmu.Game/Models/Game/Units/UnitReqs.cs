using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Game.Units;

public class UnitReqs
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    /// <summary>
    /// Possible values: AchievementObjective, AiEvent, ItemArmor, ItemWeapon, QuestComponent, Skill, Sphere
    /// </summary>
    public string OwnerType { get; set; }
    public UnitReqsKindType KindType { get; set; }
    public uint Value1 { get; set; }
    public uint Value2 { get; set; }
    public uint Value3 { get; set; }
    public bool DisplayMessage { get; set; }

    public UnitReqsValidationResult Validate(BaseUnit owner, BaseUnit target, Item targetItem = null)
    {
        var result = EvaluateKind(owner, target, targetItem);
        // The list evaluator (x2game-dev.dll 0x39796DA0) copies the failing row's display_msg into the
        // result's display gate; a passing row never reaches it.
        if (result.ResultKey != SkillResultKeys.ok)
            result.DisplayMessage = DisplayMessage;
        return result;
    }

    private UnitReqsValidationResult EvaluateKind(BaseUnit owner, BaseUnit target, Item targetItem)
    {
        UnitReqsValidationResult Ret(SkillResultKeys errorKey, bool success)
        {
            return success
                ? new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0)
                : new UnitReqsValidationResult(errorKey, 0, 0);
        }

        UnitReqsValidationResult RetWithValue(SkillResultKeys errorKey, uint value, bool success)
        {
            return success
                ? new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0)
                : new UnitReqsValidationResult(errorKey, 0, value);
        }

        // Failure whose native byte has no SkillResultKeys member yet: the key gives the closest wire byte
        // and NativeResult carries the byte the client evaluator writes (see UnitReqsValidationResult).
        UnitReqsValidationResult RetNative(SkillResult native, ushort detail, uint value, bool success)
        {
            return success
                ? new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0)
                : new UnitReqsValidationResult(SkillResultKeys.skill_failure, detail, value) { NativeResult = native };
        }

        var unit = owner as Unit;
        var targetUnit = target as Unit;
        var player = owner as Character;
        switch (KindType)
        {
            case UnitReqsKindType.Level:
                // x2game-dev.dll 0x392B13B0 reads only value1 (level >= value1); value2 is never read. The one
                // enabled row that carries a value2 (id 37420 on skill 14703, 1..10) gets the client's reading.
                return Ret(SkillResultKeys.skill_urk_level, unit != null && unit.Level >= Value1);

            case UnitReqsKindType.Ability:
                return Ret(SkillResultKeys.skill_urk_ability, player != null && player.Abilities.GetAbilityLevel((AbilityType)Value1) >= Value2);

            case UnitReqsKindType.Race:
                return Ret(SkillResultKeys.skill_urk_race, player != null && player.Race == (Race)Value1);

            case UnitReqsKindType.Gender:
                return Ret(SkillResultKeys.skill_urk_gender, player != null && player.Gender == (Gender)Value1);

            case UnitReqsKindType.EquipSlot:
                return Ret(SkillResultKeys.skill_urk_equip_slot, unit?.Equipment.GetItemBySlot((int)Value1) != null);

            case UnitReqsKindType.EquipItem:
                return Ret(SkillResultKeys.skill_urk_equip_item,
                    unit != null && unit.Equipment.GetAllItemsByTemplate(Value1, -1, out _, out _));

            case UnitReqsKindType.OwnItem:
                var ownsRequiredItem = unit?.Equipment.GetAllItemsByTemplate(
                    Value1, -1, out _, out _) ?? false;
                var ownItemContainers = Value2 > 0
                    ? new[] { SlotType.Inventory, SlotType.Bank }
                    : new[] { SlotType.Inventory };
                ownsRequiredItem |= player?.Inventory.GetAllItemsByTemplate(
                    ownItemContainers, Value1, -1, out _, out _) ?? false;
                return RetWithValue(SkillResultKeys.skill_urk_own_item, Value1,
                    ownsRequiredItem);

            case UnitReqsKindType.TrainedSkill:
                // unused
                return Ret(SkillResultKeys.skill_urk_trained_skill,
                    player != null && player.Skills.HasSkill(Value1));

            case UnitReqsKindType.Combat:
                var combatRequirementMet = unit != null && Value1 switch
                {
                    0 => !unit.IsInBattle,
                    1 => unit.IsInBattle,
                    _ => false
                };
                return Ret(SkillResultKeys.skill_urk_combat, combatRequirementMet);

            case UnitReqsKindType.Stealth:
                var isStealthed = unit?.Buffs.CheckBuffTag((uint)TagsEnum.Stealth) ?? false;
                var stealthRequirementMet = Value1 switch
                {
                    0 => !isStealthed,
                    1 => isStealthed,
                    _ => false
                };
                return Ret(SkillResultKeys.skill_urk_stealth, stealthRequirementMet);

            case UnitReqsKindType.Health:
                return Ret(SkillResultKeys.skill_urk_health,
                    unit != null && unit.Hpp >= Value1);

            case UnitReqsKindType.Buff:
                return RetWithValue(SkillResultKeys.skill_urk_buff, Value1, unit != null && unit.Buffs.CheckBuff(Value1));

            case UnitReqsKindType.TargetBuff:
                return RetWithValue(SkillResultKeys.skill_urk_target_buff, Value1, targetUnit?.Buffs.CheckBuff(Value1) ?? false);

            case UnitReqsKindType.TargetCombat:
                var targetCombatRequirementMet = targetUnit != null && Value1 switch
                {
                    0 => !targetUnit.IsInBattle,
                    1 => targetUnit.IsInBattle,
                    _ => true
                };
                return Ret(SkillResultKeys.skill_urk_target_combat, targetCombatRequirementMet);

            case UnitReqsKindType.CanLearnCraft:
                return Ret(SkillResultKeys.skill_urk_can_learn_craft,
                    player != null && CraftManager.Instance.HasCraft(Value1));

            case UnitReqsKindType.DoodadRange:
                // 0x397959C0 passes outright when value1 is the doodad sentinel (0 at 0x3D4FCA60).
                if (Value1 == 0)
                    return Ret(SkillResultKeys.ok, true);
                if (owner == null)
                    return new UnitReqsValidationResult(SkillResultKeys.skill_urk_doodad_range, 0, Value1);
                var rangeCheck = Value2 / 1000f;
                var doodads = WorldManager.GetAround<Doodad>(owner, rangeCheck * 2f, true);
                return RetWithValue(SkillResultKeys.skill_urk_doodad_range, Value1,
                    doodads.Any(doodad => owner.GetDistanceTo(doodad, true) <= rangeCheck && doodad.TemplateId == Value1));

            case UnitReqsKindType.EquipShield:
                var hasShield = unit?.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand)?.Template
                    is WeaponTemplate { HoldableTemplate.SlotTypeId: (uint)EquipmentItemSlotType.Shield };
                var offhandDisabled = unit?.Buffs.HasEffectsMatchingCondition(
                    effect => effect.Template.DisarmamentOffHand) ?? false;
                var shieldRequirementMet = Value1 switch
                {
                    0 => !hasShield,
                    1 => hasShield && !offhandDisabled,
                    _ => false
                };
                return Ret(SkillResultKeys.skill_urk_equip_shield, shieldRequirementMet);

            case UnitReqsKindType.NoBuff:
                return RetWithValue(SkillResultKeys.skill_urk_nobuff, Value1, unit != null && !unit.Buffs.CheckBuff(Value1));

            case UnitReqsKindType.TargetBuffTag:
                var targetBuffTarget = targetUnit ?? unit;
                return RetWithValue(SkillResultKeys.skill_urk_target_buff_tag, Value1, targetBuffTarget?.Buffs.CheckBuffTag(Value1) ?? false);

            // case UnitReqsKindType.CorpseRange:

            case UnitReqsKindType.EquipWeaponType:
                if (unit == null)
                    return Ret(SkillResultKeys.skill_urk_equip_weapon_type, false);
                if (Value1 == 0)
                {
                    var mainhandEmpty = unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand) == null;
                    var offhandEmpty = unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand) == null;
                    return Ret(SkillResultKeys.skill_urk_equip_weapon_type, mainhandEmpty && offhandEmpty);
                }
                var weaponSlots = new[]
                {
                    EquipmentItemSlot.Mainhand,
                    EquipmentItemSlot.Offhand,
                    EquipmentItemSlot.Ranged,
                    EquipmentItemSlot.Musical
                };
                var hasWeaponType = weaponSlots.Any(slot =>
                    unit.Equipment.GetItemBySlot((int)slot)?.Template is WeaponTemplate weapon &&
                    weapon.HoldableTemplate.Id == Value1);
                return Ret(SkillResultKeys.skill_urk_equip_weapon_type, hasWeaponType);

            case UnitReqsKindType.TargetHealthLessThan:
                // 0x392B17B0: value1 0 compares current HP, anything else the integer percent; passes at or below value2.
                return Ret(SkillResultKeys.skill_urk_target_health_less_than,
                    targetUnit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, targetUnit.Hp, targetUnit.MaxHp, Value2, lessThan: true));

            case UnitReqsKindType.TargetNpc:
                return RetWithValue(SkillResultKeys.skill_urk_target_npc, Value1,
                    targetUnit is Npc targetNpc && targetNpc.TemplateId == Value1);

            case UnitReqsKindType.TargetDoodad:
                return Ret(SkillResultKeys.skill_urk_target_doodad,
                    target is Doodad targetDoodad && targetDoodad.TemplateId == Value1);

            case UnitReqsKindType.EquipRanged:
                if (Value1 is not (0 or 1 or 2))
                    return Ret(SkillResultKeys.skill_urk_equip_ranged, false);

                if (unit == null)
                    return new UnitReqsValidationResult(
                        SkillResultKeys.skill_urk_equip_ranged,
                        0,
                        Value1 == 2 ? 3u : Value1);

                if (Value1 == 1)
                {
                    var instrument = unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Musical);
                    var isCombatInstrument = instrument?.Template is WeaponTemplate
                    {
                        HoldableTemplate.SlotTypeId: (uint)EquipmentItemSlotType.Instrument
                    };
                    if (!isCombatInstrument)
                        return new UnitReqsValidationResult(
                            SkillResultKeys.skill_urk_equip_ranged,
                            0,
                            1);

                    if (ItemManager.Instance.HasItemInstrumentSound(instrument.TemplateId))
                        return new UnitReqsValidationResult(
                            SkillResultKeys.skill_urk_equip_ranged,
                            0,
                            2);

                    var musicalSlotDisabled = unit.Buffs.HasEffectsMatchingCondition(
                        effect => effect.Template.DisarmamentMusical);
                    if (musicalSlotDisabled)
                        return new UnitReqsValidationResult(
                            SkillResultKeys.skill_urk_equip_ranged,
                            0,
                            1);

                    return Ret(SkillResultKeys.ok, true);
                }

                if (Value1 is 0 or 2)
                {
                    var requiredHoldableName = Value1 == 0 ? "bow" : "shot_gun";
                    var requiredHoldableId = ItemManager.Instance.GetConstHoldableId(requiredHoldableName);
                    var rangedWeapon = unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
                    var hasRequiredRangedWeapon = requiredHoldableId != 0 &&
                                                  rangedWeapon?.Template is WeaponTemplate rangedTemplate &&
                                                  rangedTemplate.HoldableTemplate.Id == requiredHoldableId;
                    var rangedSlotDisabled = unit.Buffs.HasEffectsMatchingCondition(
                        effect => effect.Template.DisarmamentRanged);
                    if (hasRequiredRangedWeapon && !rangedSlotDisabled)
                        return Ret(SkillResultKeys.ok, true);

                    return new UnitReqsValidationResult(
                        SkillResultKeys.skill_urk_equip_ranged,
                        0,
                        Value1 == 0 ? 0u : 3u);
                }

                return Ret(SkillResultKeys.ok, true);

            case UnitReqsKindType.NoBuffTag:
                return Ret(SkillResultKeys.skill_urk_no_buff_tag, !unit?.Buffs.CheckBuffTag(Value1) ?? false);

            case UnitReqsKindType.BuffTag:
                return RetWithValue(SkillResultKeys.skill_urk_buff_tag, Value1,
                    unit?.Buffs.CheckBuffTag(Value1) ?? false);

            case UnitReqsKindType.CompleteQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_complete_quest_context, Value1, player?.Quests.HasQuestCompleted(Value1) ?? false);

            case UnitReqsKindType.ProgressQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_progress_quest_context, Value1,
                    QuestContextUnitReqRules.IsInProgress(
                        player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Status));

            case UnitReqsKindType.ReadyQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_ready_quest_context, Value1,
                    player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Ready);

            case UnitReqsKindType.TargetNpcGroup:
                var groupTarget = targetUnit as Npc;
                var inNpcGroup = groupTarget != null &&
                    QuestManager.Instance.CheckGroupNpc(Value1, groupTarget.TemplateId);
                // 0x39795600 writes zero detail and value on failure.
                return Ret(SkillResultKeys.skill_urk_target_npc_group,
                    UnitReqTargetNpcGroupRules.Passes(groupTarget != null, inNpcGroup, Value2));

            case UnitReqsKindType.AreaSphere:
                // Check Sphere for Quest
                // NOTE: There is an exception for this check in CanUseSkill that handles this separately
                return RetWithValue(SkillResultKeys.skill_urk_area_sphere, Value1, SphereGameData.Instance.IsInsideAreaSphere(Value1, Value2, owner?.Transform?.World?.Position ?? Vector3.Zero) != null);

            case UnitReqsKindType.ExceptCompleteQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_except_complete_quest_context, Value1,
                    !player?.Quests.HasQuestCompleted(Value1) ?? false);

            case UnitReqsKindType.PreCompleteQuestContext:
                var preCompleteQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return RetWithValue(SkillResultKeys.skill_urk_precomplete_quest_context, Value1,
                    preCompleteQuest is { Step: QuestComponentKind.Progress or QuestComponentKind.Ready });

            case UnitReqsKindType.TargetOwnerType:
                return Ret(SkillResultKeys.skill_urk_target_owner_type,
                    targetUnit?.BaseUnitType == (BaseUnitType)Value1);

            case UnitReqsKindType.NotUnderWater:
                return Ret(SkillResultKeys.skill_urk_not_under_water, !unit?.IsUnderWater ?? false);

            case UnitReqsKindType.FactionMatch:
                // 0x39793B30 accepts the unit's faction or its mother faction; only kind 55 is the exact match.
                return RetWithValue(SkillResultKeys.skill_urk_faction_match, Value1,
                    unit != null && UnitReqOperatorRules.PassesFactionMatch(FactionIdOf(unit), MotherIdOf(unit), Value1));

            case UnitReqsKindType.Tod:
                var currentTime = (uint)Math.Floor(TimeManager.Instance.GetTime * 100f);
                return Ret(SkillResultKeys.skill_urk_tod, currentTime >= Value1 && currentTime <= Value2);

            case UnitReqsKindType.MotherFaction:
                // 0x397939D0 compares the root of value1 with the root of the unit's faction (0x396B6D10).
                return Ret(SkillResultKeys.skill_urk_mother_faction, SharesRootFaction(unit, Value1));

            case UnitReqsKindType.ActAbilityPoint:
                // 0x39793D00 always adds the attribute bonus and never reads value3 (all 218 rows carry 0).
                return RetWithValue(SkillResultKeys.skill_urk_actability_point, Value1,
                    player != null && player.Actability.GetPoint(Value1, true) >= Value2);

            // Kinds 44..47 and 50 share one shape in the client (0x39793E70, 0x397956B0, 0x39793EF0,
            // 0x39793F70): value1 0 needs the point at least value2, anything else at most value2, and the
            // failure carries value1 in the u32. The 27 CrimeRecord quest rows are mostly (1, 9): a record of
            // at most nine, which the old "value1 <= record <= value2" reading refused at record 0.
            case UnitReqsKindType.CrimePoint:
                return RetWithValue(SkillResultKeys.skill_urk_crime_point, Value1,
                    player != null && UnitReqOperatorRules.PassesBound(Value1, player.CrimePoint, Value2));

            case UnitReqsKindType.HonorPoint:
                return RetWithValue(SkillResultKeys.skill_urk_honor_point, Value1,
                    player != null && UnitReqOperatorRules.PassesBound(Value1, player.HonorPoint, Value2));

            case UnitReqsKindType.CrimeRecord:
                return RetWithValue(SkillResultKeys.skill_urk_crime_record, Value1,
                    player != null && UnitReqOperatorRules.PassesBound(Value1, player.CrimeRecord, Value2));

            case UnitReqsKindType.JuryPoint:
                return RetWithValue(SkillResultKeys.skill_urk_jury_point, Value1,
                    player != null && UnitReqOperatorRules.PassesBound(Value1, player.JuryPoint, Value2));

            case UnitReqsKindType.SourceOwnerType:
                return Ret(SkillResultKeys.skill_urk_source_owner_type,
                    unit?.BaseUnitType == (BaseUnitType)Value1);

            case UnitReqsKindType.Appellation:
                return RetWithValue(SkillResultKeys.skill_urk_appellation, Value1,
                    player?.Appellations.Appellations.Contains(Value1) ?? false);

            case UnitReqsKindType.LivingPoint:
                return RetWithValue(SkillResultKeys.skill_urk_living_point, Value1,
                    player != null && UnitReqOperatorRules.PassesBound(Value1, player.VocationPoint, Value2));

            case UnitReqsKindType.InZone:
                var inZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_in_zone, Value1, inZone?.Id == Value1);

            case UnitReqsKindType.OutZone:
                // Unused
                var outZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_out_zone, Value1, outZone?.Id != Value1);


            case UnitReqsKindType.VerdictOnly:
                // 0x39794130 passes while the character block field that kind 47 reads as the jury point is non-zero.
                return Ret(SkillResultKeys.skill_urk_verdict_only, player != null && player.JuryPoint != 0);

            case UnitReqsKindType.FactionMatchOnly:
                // Is this the same as UnitReqsKindType.FactionMatch ? 
                return RetWithValue(SkillResultKeys.skill_urk_faction_match_only, Value1, (uint)(unit?.Faction?.Id ?? 0) == Value1);

            case UnitReqsKindType.MotherFactionOnly:
                // 0x39793A30 is the same root comparison as kind 42 with its own result byte.
                return Ret(SkillResultKeys.skill_urk_mother_faction_only, SharesRootFaction(unit, Value1));

            case UnitReqsKindType.FactionMatchOnlyNot:
                return Ret(SkillResultKeys.skill_urk_faction_match_only_not, (uint)(unit?.Faction?.Id ?? 0) != Value1);

            case UnitReqsKindType.MotherFactionOnlyNot:
                // 0x39793A90: the negation of kind 56, value1 in the u32; a missing unit fails like the client.
                return RetWithValue(SkillResultKeys.skill_urk_mother_faction_only_not, Value1,
                    unit != null && !SharesRootFaction(unit, Value1));

            case UnitReqsKindType.NationMember:
                // 0x392B1000: the unit's faction id is at least the player-nation threshold; value1 is not read.
                return Ret(SkillResultKeys.skill_urk_nation_member,
                    unit != null && UnitReqNation.IsPlayerNationMember(FactionIdOf(unit)));

            case UnitReqsKindType.NationMemberNot:
                return Ret(SkillResultKeys.skill_urk_nation_member_not,
                    unit != null && !UnitReqNation.IsPlayerNationMember(FactionIdOf(unit)));

            case UnitReqsKindType.DominionMemberAtPos:
                // Inline in 0x397964B0: the owner of the dominion covering the unit's current zone (dominion
                // service 0x39CFBEF0, zero when unclaimed) must equal the unit's owner key (0x396B6BE0).
                return Ret(SkillResultKeys.skill_urk_dominion_member_at_pos, IsDominionMemberAtPosition(owner, unit));

            case UnitReqsKindType.DominionMemberAtPosNot:
                // Fails with 0x82 exactly when the kind 62 comparison holds; an unresolvable unit fails closed.
                return Ret(SkillResultKeys.skill_urk_dominion_member_at_pos_not,
                    owner?.Transform != null && unit != null && !IsDominionMemberAtPosition(owner, unit));

            case UnitReqsKindType.Housing:
                // 0x397961E0 walks the housing service list and compares each template's field +0x1C, the
                // housings.category_id that kind 83 compares on the target house, with value1; value2 1 needs a
                // match, anything else needs none. The owned-house list is the server's reading of that walk.
                return RetWithValue(SkillResultKeys.skill_urk_housing, Value1,
                    player != null && UnitReqOperatorRules.PassesHousing(Value2,
                        HousingManager.Instance.OwnsHouseOfCategory(player.Id, Value1)));

            case UnitReqsKindType.HealthMargin:
                // 0x392B18F0: max HP minus current HP must reach value1, failing 0x84 with value1
                // (no enabled rows in 10.0.2.13, and no key maps to 0x84 yet).
                return RetNative(SkillResult.UrkHealthMargin, 0, Value1,
                    unit != null && UnitReqOperatorRules.PassesMargin(unit.MaxHp, unit.Hp, Value1));

            case UnitReqsKindType.ManaMargin:
                // 0x392B1970: max MP minus current MP must reach value1 (no enabled rows in 10.0.2.13).
                return RetWithValue(SkillResultKeys.skill_urk_mana_margin, Value1,
                    unit != null && UnitReqOperatorRules.PassesMargin(unit.MaxMp, unit.Mp, Value1));

            case UnitReqsKindType.LaborPowerMargin:
                // 0x39794590 asks the labor service for max and current with selector 0, the account pool
                // (selector 1 is the local pool of kind 99), and passes when max - current >= value1.
                if (player == null)
                    return RetWithValue(SkillResultKeys.skill_urk_labor_power_margin, Value1, false);
                var accountLaborCap = TimedRewardsManager.GetMaxLabor(player.PremiumGrade, player.Connection?.Payment?.PremiumState ?? false, player.AccountId);
                return RetWithValue(SkillResultKeys.skill_urk_labor_power_margin, Value1,
                    UnitReqOperatorRules.PassesMargin(accountLaborCap, player.LaborPower, Value1));

            case UnitReqsKindType.LaborPowerMarginLocal:
                var remainingLocalLaborMargin = player != null
                    ? player.MaxLocalLaborPower - player.LocalLaborPower
                    : -1;
                return RetWithValue(
                    SkillResultKeys.skill_urk_labor_power_margin_local,
                    Value1,
                    remainingLocalLaborMargin >= 0 && (ulong)remainingLocalLaborMargin >= Value1);

            case UnitReqsKindType.NotOnMovingPhysicalVehicle:
                return UnsupportedRequirement();

            case UnitReqsKindType.MaxLevel:
                return Ret(SkillResultKeys.skill_urk_max_level, player?.Level <= Value1);

            // The three leadership kinds (0x39795780, 0x39794380, 0x39795810) read value1 as the bound
            // selector and value2 as the threshold, and all fail with result 0x90, detail 0x355
            // (enum_error_messages 853 WRONG_LEADERSHIP_POINT) and value1 in the u32.
            case UnitReqsKindType.LeadershipTotal:
                if (player != null && UnitReqOperatorRules.PassesBound(Value1, player.AccumulatedLeadershipPoint, Value2))
                    return Ret(SkillResultKeys.skill_urk_leadership_total, true);
                return new UnitReqsValidationResult(SkillResultKeys.skill_urk_leadership_total, LeadershipFailureDetail, Value1);

            case UnitReqsKindType.LeadershipCurrent:
                // Kind 78 also writes 0x90 (the leadership-total byte), so it reports through that key.
                if (player != null && UnitReqOperatorRules.PassesBound(Value1, player.LeadershipPoint, Value2))
                    return Ret(SkillResultKeys.skill_urk_leadership_current, true);
                return new UnitReqsValidationResult(SkillResultKeys.skill_urk_leadership_total, LeadershipFailureDetail, Value1);

            case UnitReqsKindType.LeadershipPeriod:
                if (player != null && UnitReqOperatorRules.PassesBound(Value1, player.LeadershipPeriodPoint, Value2))
                    return Ret(SkillResultKeys.skill_urk_leadership_period, true);
                // The native evaluator (x2game-dev.dll FUN_39795810) fails this kind with result 0x90, detail
                // 0x355 (enum_error_messages 853 WRONG_LEADERSHIP_POINT) and the row's value1 in the u32.
                const ushort leadershipPeriodFailureDetail = 0x355;
                return new UnitReqsValidationResult(
                    SkillResultKeys.skill_urk_leadership_period,
                    leadershipPeriodFailureDetail,
                    Value1);

            case UnitReqsKindType.Hero:
                // 0x39794430: value1 0 passes any seated hero; otherwise the seated record's grade byte must
                // equal value1 (hero_grades tier, ItemArmor rows use 1..4). Failure: detail 0x356, value1 in the u32.
                var heroPasses = player != null && UnitReqOperatorRules.PassesHero(
                    Value1,
                    Value1 == 0 && HeroManager.Instance.IsCurrentHero(player),
                    Value1 == 0 ? 0 : HeroManager.Instance.GradeOf(player));
                return heroPasses
                    ? Ret(SkillResultKeys.skill_urk_hero, true)
                    : new UnitReqsValidationResult(SkillResultKeys.skill_urk_hero, HeroFailureDetail, Value1);

            case UnitReqsKindType.NotHero:
                return Ret(SkillResultKeys.skill_urk_not_hero, player != null && !HeroManager.Instance.IsCurrentHero(player));

            case UnitReqsKindType.NotHeroNotCandidate:
                return Ret(SkillResultKeys.skill_urk_not_hero_not_candidate,
                    player != null && !HeroManager.Instance.IsCurrentHero(player) && !HeroManager.Instance.IsCandidate(player));

            case UnitReqsKindType.ExpeditionOwner:
                return Ret(SkillResultKeys.skill_urk_expedition_owner,
                    player != null && player.Expedition?.OwnerId == player.Id);

            case UnitReqsKindType.ExpeditionMember:
                // 0x39794230: no expedition fails with detail 0x328; then the member role byte (owner 255,
                // faction service +0x370) must reach value1, else detail 0x506. The 255 rows are owner-only skills.
                var expeditionMemberDetail = UnitReqOperatorRules.ExpeditionMemberDetail(
                    player?.Expedition?.GetMember(player)?.Role, Value1);
                return expeditionMemberDetail == 0
                    ? Ret(SkillResultKeys.skill_urk_expedition_member, true)
                    : new UnitReqsValidationResult(SkillResultKeys.skill_urk_expedition_member, expeditionMemberDetail, 0);

            case UnitReqsKindType.ExceptProgressQuestContext:
                var exceptProgressActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return RetWithValue(SkillResultKeys.skill_urk_except_progress_quest_context, Value1,
                    player != null && exceptProgressActiveQuest is not { Step: QuestComponentKind.Progress });

            case UnitReqsKindType.ExceptReadyQuestContext:
                var exceptReadyActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return RetWithValue(SkillResultKeys.skill_urk_except_ready_quest_context, Value1,
                    player != null && exceptReadyActiveQuest is not { Step: QuestComponentKind.Ready });

            case UnitReqsKindType.OwnItemNot:
                var ownsExcludedItem = unit?.Equipment.GetAllItemsByTemplate(
                    Value1, -1, out _, out _) ?? false;
                var searchedContainers = Value2 > 0
                    ? new[] { SlotType.Inventory, SlotType.Bank }
                    : new[] { SlotType.Inventory };
                ownsExcludedItem |= player?.Inventory.GetAllItemsByTemplate(
                    searchedContainers, Value1, -1, out _, out _) ?? false;
                return RetWithValue(SkillResultKeys.skill_urk_own_item_not, Value1,
                    !ownsExcludedItem);

            case UnitReqsKindType.LessActAbilityPoint:
                // 0x39793DB0 adds the attribute bonus only when value3 is 0 and packs value1 * 0x2000000 + value2 into the u32.
                return RetWithValue(SkillResultKeys.skill_urk_less_actability_point,
                    UnitReqOperatorRules.LessActAbilityDetail(Value1, Value2),
                    player != null && player.Actability.GetPoint(Value1, Value3 == 0) < Value2);

            case UnitReqsKindType.OwnQuestItemGroup:
                return Ret(SkillResultKeys.skill_urk_own_quest_item_group,
                    player != null && QuestManager.Instance.GetGroupItems(Value1).Any(entry =>
                        player.Inventory.GetAllItemsByTemplate(null, entry.ItemId, -1, out _, out _)));

            case UnitReqsKindType.House:
                if (target is not House { Template: not null } targetHouse)
                    return Ret(SkillResultKeys.skill_urk_house_only, false);
                var categoryMatches = targetHouse.Template.CategoryId == Value1;
                return Ret(SkillResultKeys.skill_urk_house,
                    (Value2 == 1) == categoryMatches);

            case UnitReqsKindType.DoodadTargetHostile:
                if (owner?.Faction == null || target is not Doodad hostileDoodad)
                    return Ret(SkillResultKeys.skill_urk_doodad_target_hostile, false);
                var doodadFaction = DoodadManager.Instance.GetEffectiveFaction(hostileDoodad);
                return Ret(SkillResultKeys.skill_urk_doodad_target_hostile,
                    doodadFaction != null &&
                    owner.Faction.GetRelationState(doodadFaction) == RelationState.Hostile);

            case UnitReqsKindType.TargetNoBuffTag:
                if (targetUnit == null)
                    return Ret(SkillResultKeys.skill_urk_target_nobuff_tag_no_target, false);
                return RetWithValue(SkillResultKeys.skill_urk_target_nobuff_tag, Value1,
                    !targetUnit.Buffs.CheckBuffTag(Value1));

            case UnitReqsKindType.UnderWater:
                return Ret(SkillResultKeys.skill_urk_under_water, unit?.IsUnderWater ?? false);

            case UnitReqsKindType.OwnAppellation:
                // 0x397958A0 passes outright when value1 is the appellation sentinel (0 at 0x3D4FCA68).
                return RetWithValue(SkillResultKeys.skill_urk_own_appellation, Value1,
                    Value1 == 0 || (player?.Appellations.Appellations.Contains(Value1) ?? false));

            case UnitReqsKindType.EquipAppellation:
                // 0x39794970: same sentinel, then the active appellation must be value1.
                return RetWithValue(SkillResultKeys.skill_urk_equip_appellation, Value1,
                    Value1 == 0 || player?.Appellations.ActiveAppellation == Value1);

            case UnitReqsKindType.EmptySlotInventory:
                if (player?.Inventory.Bag.FreeSlotCount > 0)
                    return Ret(SkillResultKeys.skill_urk_empty_slot_inventory, true);
                // The native evaluator writes 0x19 to the result's 16-bit detail field for a full bag.
                const ushort emptyInventorySlotFailureDetail = 0x19;
                return new UnitReqsValidationResult(
                    SkillResultKeys.skill_urk_empty_slot_inventory,
                    emptyInventorySlotFailureDetail,
                    0);

            case UnitReqsKindType.HeirLevel:
                // 0x392B1400 writes zero detail and value on failure.
                return Ret(SkillResultKeys.skill_urk_heir_level, unit?.HeirLevel >= Value1);

            case UnitReqsKindType.InZoneGroup:
                var currentZoneGroup = owner?.Transform != null
                    ? ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId)?.GroupId
                    : null;
                return RetWithValue(SkillResultKeys.skill_urk_in_zone_group, Value1,
                    currentZoneGroup == Value1);

            case UnitReqsKindType.SkillCooldown:
                return Ret(SkillResultKeys.skill_urk_skill_cooldown,
                    unit?.Cooldowns.CheckCooldown(Value1) ?? false);

            case UnitReqsKindType.FullRechargedLaborPower:
                // 0x39794A60 fails with 0xB1 once the pool is at its cap, so a labor potion can be drunk only
                // while there is room. The nine owning skills pair with AddLaborPower rows whose second value
                // is 1, the character-local pool (premium_grades.max_local_labor).
                return Ret(SkillResultKeys.skill_urk_full_recharged_labor_power,
                    player != null && UnitReqOperatorRules.PassesNotFullyRecharged(player.LocalLaborPower, player.MaxLocalLaborPower));

            case UnitReqsKindType.ExpeditionMemberNot:
                return Ret(SkillResultKeys.skill_urk_expedition_member_not,
                    player != null && player.Expedition == null);

            case UnitReqsKindType.RaidOwner:
                var ownerRaid = player != null
                    ? TeamManager.Instance.GetActiveTeamByUnit(player.Id)
                    : null;
                return Ret(SkillResultKeys.skill_failure,
                    ownerRaid is { IsParty: false } && ownerRaid.OwnerId == player.Id);

            case UnitReqsKindType.ViceRaidOwner:
                // Vice owners exist only in the client's joint-raid hierarchy. An ordinary raid
                // has no vice-owner role, so the requirement correctly fails when no joint raid exists.
                return Ret(SkillResultKeys.skill_failure, false);

            case UnitReqsKindType.RaidMember:
                var memberRaid = player != null
                    ? TeamManager.Instance.GetActiveTeamByUnit(player.Id)
                    : null;
                return Ret(SkillResultKeys.skill_failure,
                    memberRaid is { IsParty: false } && memberRaid.OwnerId != player.Id);

            case UnitReqsKindType.Dual:
                // "Dual" is the duel: 0x39795D60 reads the unit's duel id from the same combat component that
                // kind 117 reads the expedition battle from (vtable +0x68 versus +0x80) and compares it with the
                // zero sentinel at 0x3D4FCA6C. value1 1 tests the target, value2 0 needs a duel (URK_DUAL),
                // 1 needs none (URK_NO_DUAL), anything else passes. Owners: 40364 "for the duel", 43061.
                var duelUnit = Value1 == 1 ? targetUnit : unit;
                if (duelUnit == null)
                    return Ret(SkillResultKeys.skill_failure, false);
                return Ret(Value2 == 0 ? SkillResultKeys.skill_urk_dual : SkillResultKeys.skill_urk_no_dual,
                    UnitReqOperatorRules.PassesStateGate(Value2, duelUnit.IsInDuel));

            case UnitReqsKindType.TargetItemTag:
                return RetWithValue(SkillResultKeys.skill_urk_target_item_tag, Value1,
                    targetItem != null && ItemManager.Instance.HasItemTag(targetItem.TemplateId, Value1));

            case UnitReqsKindType.NoTargetItemTag:
                return RetWithValue(SkillResultKeys.skill_urk_no_target_item_tag, Value1,
                    targetItem != null && !ItemManager.Instance.HasItemTag(targetItem.TemplateId, Value1));

            case UnitReqsKindType.EquipItemTag:
                if (unit == null || !TagsGameData.Instance.Exists(Value1))
                    return Ret(SkillResultKeys.skill_failure, false);

                for (var slot = 0; slot < EquipmentSerializer.SlotCount; slot++)
                {
                    var equippedItem = unit.Equipment.GetItemBySlot(slot);
                    if (equippedItem != null && ItemManager.Instance.HasItemTag(equippedItem.TemplateId, Value1))
                        return Ret(SkillResultKeys.ok, true);
                }

                return Ret(SkillResultKeys.skill_failure, false);

            case UnitReqsKindType.NoEquipItemTag:
                // 0x392B1210 is the mirror of kind 134: the tag must exist and no equipped item may carry it;
                // both failures are plain FAILURE. Its 45 rows sit on ItemArmor/ItemAccessory owners.
                if (unit == null || !TagsGameData.Instance.Exists(Value1))
                    return Ret(SkillResultKeys.skill_failure, false);

                for (var slot = 0; slot < EquipmentSerializer.SlotCount; slot++)
                {
                    var taggedItem = unit.Equipment.GetItemBySlot(slot);
                    if (taggedItem != null && ItemManager.Instance.HasItemTag(taggedItem.TemplateId, Value1))
                        return Ret(SkillResultKeys.skill_failure, false);
                }

                return Ret(SkillResultKeys.ok, true);

            case UnitReqsKindType.CombatResource:
                if (unit == null)
                    return Ret(SkillResultKeys.skill_invalid_source, false);
                return Ret(SkillResultKeys.skill_urk_combat_resource,
                    (long)unit.GetCombatResource((int)Value1) >= Value2);

            // Kinds 95..97 and 138/139 (0x392B1A50, 0x392B1AF0, 0x392B1B90, 0x392B1850, 0x392B1C30) share the
            // kind 26 shape: value1 0 compares the absolute pool, anything else the integer percent, against value2.
            case UnitReqsKindType.TargetManaLessThan:
                return Ret(SkillResultKeys.skill_urk_target_mana_less_than,
                    targetUnit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, targetUnit.Mp, targetUnit.MaxMp, Value2, lessThan: true));

            case UnitReqsKindType.TargetManaMoreThan:
                return Ret(SkillResultKeys.skill_urk_target_mana_more_than,
                    targetUnit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, targetUnit.Mp, targetUnit.MaxMp, Value2, lessThan: false));

            case UnitReqsKindType.TargetHealthMoreThan:
                return Ret(SkillResultKeys.skill_urk_target_health_more_than,
                    targetUnit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, targetUnit.Hp, targetUnit.MaxHp, Value2, lessThan: false));

            case UnitReqsKindType.SourceHealthLessThan:
                return Ret(SkillResultKeys.skill_urk_source_health_less_than,
                    unit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, unit.Hp, unit.MaxHp, Value2, lessThan: true));

            case UnitReqsKindType.SourceHealthMoreThan:
                return Ret(SkillResultKeys.skill_urk_source_health_more_than,
                    unit != null && UnitReqOperatorRules.PassesPoolCompare(Value1, unit.Hp, unit.MaxHp, Value2, lessThan: false));

            case UnitReqsKindType.FamilyRole:
                var family = player?.Family > 0
                    ? FamilyManager.Instance.GetFamily(player.Family)
                    : null;
                if (family?.Members.Any(member => member.Id == player.Id && member.Role == Value1) == true)
                    return Ret(SkillResultKeys.skill_urk_family_role, true);
                // The native evaluator writes 0x3d0 to the result's 16-bit detail field on failure.
                const ushort familyRoleFailureDetail = 0x3D0;
                return new UnitReqsValidationResult(
                    SkillResultKeys.skill_urk_family_role,
                    familyRoleFailureDetail,
                    0);

            case UnitReqsKindType.OwnItemCount:
                var ownedItemCount = 0;
                if (unit != null)
                {
                    unit.Equipment.GetAllItemsByTemplate(Value1, -1, out _, out var equippedItemCount);
                    ownedItemCount += equippedItemCount;
                }
                if (player != null)
                {
                    player.Inventory.GetAllItemsByTemplate(
                        [SlotType.Inventory], Value1, -1, out _, out var inventoryItemCount);
                    ownedItemCount += inventoryItemCount;
                }
                return RetWithValue(SkillResultKeys.skill_urk_own_item_count, Value1,
                    player != null && ownedItemCount >= Value2);

            case UnitReqsKindType.NotHousingArea:
                var world = owner?.ParentWorld ?? (owner?.Transform != null
                    ? WorldManager.Instance.GetWorld(owner.Transform.InstanceId)
                    : null);
                var position = owner?.Transform?.World.Position ?? Vector3.Zero;
                return Ret(SkillResultKeys.skill_urk_not_in_housing_area,
                    world != null && SubZoneManager.Instance
                        .GetHousingZoneByPosition(world, position.X, position.Y).Count == 0);

            case UnitReqsKindType.PremiumArchePass:
                // 0x39794E60 reads no operand: the pass in progress must carry the premium flag.
                return Ret(SkillResultKeys.skill_urk_premium_arche_pass,
                    player?.ArchePass?.HasPremium() == true);

            case UnitReqsKindType.EnableArchePass:
                // 0x39794EE0: no pass in progress is URK_ENABLE_ARCHE_PASS; value1 0 (sentinel 0x3D4FCA74)
                // accepts any pass, otherwise a pass in progress that is not value1 is ..._WITH_TYPE.
                var anyArchePass = player?.ArchePass?.HasProgress() == true;
                return Ret(
                    anyArchePass
                        ? SkillResultKeys.skill_urk_enable_arche_pass_with_type
                        : SkillResultKeys.skill_urk_enable_arche_pass,
                    UnitReqOperatorRules.PassesEnableArchePass(Value1, anyArchePass, Value1 != 0 && player.ArchePass.HasProgress(Value1)));

            case UnitReqsKindType.Ulc:
                if (player == null || !UlcGameData.Instance.Exists(Value1))
                    return Ret(SkillResultKeys.skill_failure, false);

                var expectsActiveUlc = Value2 == 1;
                var hasActiveUlc = AccountAttributeManager.Instance
                    .Get(player.AccountId, AppConfiguration.Instance.Id)
                    .Any(attribute =>
                        attribute.KindId == (uint)AccountAttributeKind.Ulc &&
                        attribute.KindValue == Value1);
                if (hasActiveUlc == expectsActiveUlc)
                    return Ret(SkillResultKeys.ok, true);

                return RetWithValue(
                    expectsActiveUlc
                        ? SkillResultKeys.skill_urk_need_ulc_activate
                        : SkillResultKeys.skill_urk_cannot_use_by_ulc_activate,
                    Value1,
                    false);

            case UnitReqsKindType.DominionMember:
                // Inline in 0x397964B0: the unit's owner key holds at least one dominion (0x39CFBF30); fails 0x9C.
                return RetNative(SkillResult.UrkDominionMember, 0, 0, unit != null && DominionCountOf(unit) > 0);

            case UnitReqsKindType.DominionMemberNot:
                // Fails 0x9D when the owner key holds any dominion (no enabled rows in 10.0.2.13).
                return RetNative(SkillResult.UrkDominionMemberNot, 0, 0, unit != null && DominionCountOf(unit) == 0);

            case UnitReqsKindType.InZoneGroupHousingExist:
                // The client evaluator passes this kind unconditionally, so the rule is the server's. value1 is
                // housings.id (654/655/656 territory castle tiers, 135/170 castle gates) and value2 is 1 on all
                // 73 rows, which sit on the territory warehouse specialty skills in OR groups per tier. The
                // reading here is that the house must stand in the unit's zone group; any other value2 fails closed.
                if (Value2 != 1)
                    return UnsupportedRequirement();
                var housingGroupId = CurrentZoneGroupId(owner);
                return Ret(SkillResultKeys.skill_failure,
                    housingGroupId != null && HousingManager.Instance.HasHouseTemplateInZoneGroup(Value1, housingGroupId.Value));

            case UnitReqsKindType.ExpeditionLevel:
                // 0x39794830: the expedition level lies within value1..value2 inclusive (either order); a
                // character without an expedition reads level 0. Failure: 0xA1 with detail 0x3A1.
                return RetNative((SkillResult)ExpeditionLevelNativeResult, ExpeditionLevelFailureDetail, 0,
                    player != null && UnitReqOperatorRules.PassesExpeditionLevel(Value1, Value2, player.Expedition?.Level ?? 0));

            case UnitReqsKindType.IsResident:
                // 0x39795B70: value1 0 (sentinel 0x3D4FCA3C) means the current zone group. value2 0 is the
                // resident-map lookup (0x39173FE0), fail 0xA2 / 0x3C2. The value2 != 0 branch keys another map
                // by zone_groups field +0x44 and has no enabled rows, so it stays closed.
                if (Value2 != 0)
                    return UnsupportedRequirement();
                var residentGroupId = Value1 == 0 ? CurrentZoneGroupId(owner) : Value1;
                return RetNative((SkillResult)IsResidentNativeResult, ResidentFailureDetail, 0,
                    player != null && residentGroupId != null &&
                    HousingManager.Instance.IsResidentOfZoneGroup(player.Id, residentGroupId.Value));

            case UnitReqsKindType.ResidentServicePoint:
                // 0x39795C50: resident of the zone group (else 0xA3 / 0x3C2), then service points at least value2
                // (else 0xA3 / 0x3C3). The points are the character's settled resident state for the group.
                var serviceGroupId = Value1 == 0 ? CurrentZoneGroupId(owner) : Value1;
                var isServiceResident = player != null && serviceGroupId != null &&
                                        HousingManager.Instance.IsResidentOfZoneGroup(player.Id, serviceGroupId.Value);
                if (!isServiceResident)
                    return RetNative((SkillResult)ResidentServicePointNativeResult, ResidentFailureDetail, 0, false);
                var modelledResidentServicePoints = ResidentManager.Instance
                    .GetServicePoint(player.Id, (ushort)serviceGroupId.Value);
                return RetNative((SkillResult)ResidentServicePointNativeResult, ResidentServicePointFailureDetail, 0,
                    modelledResidentServicePoints >= Value2);

            case UnitReqsKindType.AchievementComplete:
                // 0x39794C70: the achievement record for value1 must carry a completion time; fails 0xB4 with value1.
                return RetNative(SkillResult.UrkAchievementComplete, 0, Value1,
                    player?.Achievements?.IsComplete(Value1) == true);

            case UnitReqsKindType.ExpeditionBattle:
                // 0x39795E70: value1 1 tests the target; the unit's expedition battle id (combat component
                // vtable +0x80) against the zero sentinel. value2 0 needs a battle (0xB9), 1 needs none (0xBA),
                // anything else passes. The one row (skill 40364, value2 1) refuses a duel during a guild war.
                var battleUnit = Value1 == 1 ? targetUnit : unit;
                if (battleUnit == null)
                    return Ret(SkillResultKeys.skill_failure, false);
                var inExpeditionBattle = (battleUnit as Character)?.Expedition?.IsAtWar == true;
                return RetNative(Value2 == 0 ? SkillResult.UrkExpeditionBattle : SkillResult.UrkNoExpeditionBattle, 0, 0,
                    UnitReqOperatorRules.PassesStateGate(Value2, inExpeditionBattle));

            case UnitReqsKindType.DominionCount:
                // 0x39794DF0 counts the dominions held by the unit's owner key (0x396ADAB0): value2 0 needs at
                // least value1 (0xBD), anything else at most value1 (0xBC). No source is INVALID_SOURCE.
                if (unit == null)
                    return Ret(SkillResultKeys.skill_invalid_source, false);
                return RetNative(Value2 == 0 ? SkillResult.UrkDominionCountLess : SkillResult.UrkDominionCountMore, 0, 0,
                    UnitReqOperatorRules.PassesDominionCount(Value2, DominionCountOf(unit), Value1));

            case UnitReqsKindType.GearScore:
                // 0x392B0D50: value1 0 needs gear score at least value2, anything else at most; the failure u32
                // is value2, negated for the upper bound. Only characters carry a gear score.
                return RetNative(SkillResult.UrkGearScore, 0, UnitReqOperatorRules.GearScoreDetail(Value1, Value2),
                    player != null && UnitReqOperatorRules.PassesGearScore(Value1, player.GearScore, Value2));

            case UnitReqsKindType.FactionPower:
                // 0x39795F80 tests whether faction value1 is in the client's faction-power set, filled from the
                // faction power score feed the server does not model (SCFactionPowerScore is sent as zeros).
                return MissingState("faction power scores are not modelled");

            case UnitReqsKindType.FactionChangePossibleFromTo:
                // 0x39794F90 passes when the faction service's per-(from, to) quota record (0x39CD5C90: limit
                // minus two counters) is positive. That record is server-fed and the server keeps no such quota.
                return MissingState("faction change quotas are not modelled");

            case UnitReqsKindType.FactionChangeCooldown:
                // 0x39794FF0 passes once the character's faction change cooldown end (character block +0x3B60)
                // is in the past. The server keeps no faction change timestamp.
                return MissingState("faction change cooldown is not modelled");

            case UnitReqsKindType.ConflictZoneState:
                // 0x39795130 needs a conflict record for the unit's zone group and compares the state test
                // selected by value1 with value2 (see UnitReqOperatorRules.PassesConflictZoneState).
                if (owner == null)
                    return Ret(SkillResultKeys.skill_invalid_source, false);
                var conflictGroupId = CurrentZoneGroupId(owner);
                var zoneConflict = conflictGroupId != null ? ZoneManager.Instance.GetZoneGroupById(conflictGroupId.Value)?.Conflict : null;
                return RetNative(SkillResult.UrkConflictZoneState, 0, 0,
                    zoneConflict != null && UnitReqOperatorRules.PassesConflictZoneState(Value1, Value2, zoneConflict.CurrentZoneState));

            case UnitReqsKindType.ZoneScoreLevel:
                // 0x397962C0: value1 is zone_score_kinds.id, value3 the level from zone_score_levels, value2 the
                // comparison (0 at least, 1 at most, 2 equal). The per-character score map (block +0x3BC0) is
                // not modelled server-side.
                return MissingState("zone scores are not modelled");

            case UnitReqsKindType.ZoneScore:
                // 0x397963E0: same operands against the raw score.
                return MissingState("zone scores are not modelled");

            case UnitReqsKindType.TowerDefStep:
                // 0x39795310: value1 is the zone group, value2 the tower_defs id, value3 a one-based prog index no
                // larger than the tower's prog count; the running event's step in that group must be value3 - 1.
                if (owner == null)
                    return Ret(SkillResultKeys.skill_invalid_source, false);
                var towerProgCount = TowerDefGameData.Instance.GetTowerDef(Value2)?.Progs?.Count ?? 0;
                var towerStep = Value1 == 0 || Value2 == 0 ? null : WorldIntegration.GetTowerDefCurrentStep?.Invoke((ushort)Value1, Value2);
                return RetNative(SkillResult.UrkTowerDefStep, 0, 0,
                    UnitReqOperatorRules.PassesTowerDefStep(towerStep, Value3, towerProgCount));

            case UnitReqsKindType.VisualRaceTimeMoreThan:
                // 0x39795420 passes when the visual race expiry (unit +0x1C28) is at least value1 hours past
                // XlGetCurrentFileTime. The server only echoes the client's creation-time value for that field
                // and has no clock in its units, so the one row (skill 47673, 696 h) fails closed.
                return MissingState("visual race expiry clock is not modelled");

            default:
                return UnsupportedRequirement();
        }

        UnitReqsValidationResult UnsupportedRequirement()
        {
            Logger.Warn(
                "Unsupported UnitReq blocked: id={0} owner={1}:{2} kind={3} values={4},{5},{6}",
                Id, OwnerType, OwnerId, KindType, Value1, Value2, Value3);
            return new UnitReqsValidationResult(SkillResultKeys.skill_urk_unknown, 0, 0);
        }

        // The rule is recovered (see the case comment) but the server holds no source for its input. The
        // native handlers of these kinds all fail with plain FAILURE.
        UnitReqsValidationResult MissingState(string missing)
        {
            Logger.Debug(
                "UnitReq {0} failed closed, {1}: id={2} owner={3}:{4} values={5},{6},{7}",
                KindType, missing, Id, OwnerType, OwnerId, Value1, Value2, Value3);
            return new UnitReqsValidationResult(SkillResultKeys.skill_failure, 0, 0);
        }

        static uint FactionIdOf(Unit unit) => (uint)(unit?.Faction?.Id ?? 0);

        static uint MotherIdOf(Unit unit) => (uint)(unit?.Faction?.MotherId ?? 0);

        // Root comparison of kinds 42/56/59: the faction service walk (0x39CCD710) returns the input id when
        // the faction is unknown, so an unlisted value1 is its own root.
        static bool SharesRootFaction(Unit unit, uint value1)
        {
            if (unit == null)
                return false;
            var required = FactionManager.Instance.GetFaction((FactionsEnum)value1);
            return UnitReqOperatorRules.SharesRootFaction(
                FactionIdOf(unit), MotherIdOf(unit), value1, (uint)(required?.MotherId ?? 0));
        }

        static uint? CurrentZoneGroupId(BaseUnit owner)
        {
            return owner?.Transform != null
                ? ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId)?.GroupId
                : null;
        }

        static uint DominionOwnerKeyOf(Unit unit)
        {
            return UnitReqOperatorRules.DominionOwnerKey(
                (uint)((unit as Character)?.Expedition?.Id ?? 0), FactionIdOf(unit), MotherIdOf(unit));
        }

        // Claims live in two registries: siege zones in DominionManager, the guild-only zone groups in
        // GuildDominionManager; both key by the house's zone key, which is what Transform.ZoneId holds.
        static bool IsDominionMemberAtPosition(BaseUnit owner, Unit unit)
        {
            if (owner?.Transform == null || unit == null)
                return false;
            var zoneKey = (ushort)owner.Transform.ZoneId;
            var dominion = DominionManager.Instance.GetByZoneId(zoneKey) ?? GuildDominionManager.Instance.GetByZoneId(zoneKey);
            return dominion != null && UnitReqOperatorRules.IsDominionOwnedBy(
                dominion.ExpeditionId, dominion.OwningFactionId, DominionOwnerKeyOf(unit));
        }

        static int DominionCountOf(Unit unit)
        {
            var ownerKey = DominionOwnerKeyOf(unit);
            return DominionManager.Instance.Dominions
                .Concat(GuildDominionManager.Instance.GuildDominions)
                .Count(dominion => UnitReqOperatorRules.IsDominionOwnedBy(dominion.ExpeditionId, dominion.OwningFactionId, ownerKey));
        }
    }

    /// <summary>Detail the three leadership kinds write on failure (enum_error_messages 853 WRONG_LEADERSHIP_POINT).</summary>
    private const ushort LeadershipFailureDetail = 0x355;

    /// <summary>Detail kind 79 writes on failure (x2game-dev.dll 0x39794430).</summary>
    private const ushort HeroFailureDetail = 0x356;

    /// <summary>Detail kind 90 writes on failure (0x39794830).</summary>
    private const ushort ExpeditionLevelFailureDetail = 0x3A1;

    /// <summary>Detail kinds 91 and 92 write when the character is not a resident (0x39795B70, 0x39795C50).</summary>
    private const ushort ResidentFailureDetail = 0x3C2;

    /// <summary>Detail kind 92 writes when the service points are short (0x39795C50).</summary>
    private const ushort ResidentServicePointFailureDetail = 0x3C3;

    // Result bytes the evaluators of kinds 90, 91 and 92 write. The client's result-to-symbol switch
    // (0x39D23B10) has no case for 0xA1..0xA3, and SkillResult has no member for them yet.
    private const byte ExpeditionLevelNativeResult = 0xA1;
    private const byte IsResidentNativeResult = 0xA2;
    private const byte ResidentServicePointNativeResult = 0xA3;
}
