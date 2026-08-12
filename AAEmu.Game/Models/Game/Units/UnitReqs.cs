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

        var unit = owner as Unit;
        var targetUnit = target as Unit;
        var player = owner as Character;
        switch (KindType)
        {
            case UnitReqsKindType.Level:
                return Ret(SkillResultKeys.skill_urk_level, unit != null && unit.Level >= Value1 && (Value2 == 0 || unit.Level <= Value2));

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
                    player?.Skills.Skills.GetValueOrDefault(Value1) != null);

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
                return Ret(SkillResultKeys.skill_urk_target_health_less_than,
                    targetUnit?.Hpp >= Value1 && targetUnit.Hpp <= Value2);

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
                    player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Progress);

            case UnitReqsKindType.ReadyQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_ready_quest_context, Value1,
                    player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Ready);

            case UnitReqsKindType.TargetNpcGroup:
                return RetWithValue(SkillResultKeys.skill_urk_target_npc_group, Value1,
                    targetUnit is Npc groupTarget &&
                    QuestManager.Instance.CheckGroupNpc(Value1, groupTarget.TemplateId));

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
                return RetWithValue(SkillResultKeys.skill_urk_faction_match, Value1, (uint)(unit?.Faction?.Id ?? 0) == Value1);

            case UnitReqsKindType.Tod:
                var currentTime = (uint)Math.Floor(TimeManager.Instance.GetTime * 100f);
                return Ret(SkillResultKeys.skill_urk_tod, currentTime >= Value1 && currentTime <= Value2);

            case UnitReqsKindType.MotherFaction:
                return Ret(SkillResultKeys.skill_urk_mother_faction, (uint)(unit?.Faction.MotherId ?? 0) == Value1);

            case UnitReqsKindType.ActAbilityPoint:
                return RetWithValue(SkillResultKeys.skill_urk_actability_point, Value1,
                    player != null && player.Actability.GetPoint(Value1, Value3 == 0) >= Value2);

            case UnitReqsKindType.CrimePoint:
                return Ret(SkillResultKeys.skill_urk_crime_point,
                    player != null && player.CrimePoint >= Value1 &&
                    (Value2 == 0 || player.CrimePoint <= Value2));

            case UnitReqsKindType.HonorPoint:
                return Ret(SkillResultKeys.skill_urk_honor_point,
                    player?.HonorPoint >= Value1 && player.HonorPoint <= Value2);

            case UnitReqsKindType.CrimeRecord:
                return Ret(SkillResultKeys.skill_urk_crime_record,
                    player != null && player.CrimeRecord >= Value1 &&
                    (Value2 == 0 || player.CrimeRecord <= Value2));

            case UnitReqsKindType.JuryPoint:
                return Ret(SkillResultKeys.skill_urk_jury_point,
                    player != null && player.JuryPoint >= Value1 &&
                    (Value2 == 0 || player.JuryPoint <= Value2));

            case UnitReqsKindType.SourceOwnerType:
                return Ret(SkillResultKeys.skill_urk_source_owner_type,
                    unit?.BaseUnitType == (BaseUnitType)Value1);

            case UnitReqsKindType.Appellation:
                return RetWithValue(SkillResultKeys.skill_urk_appellation, Value1,
                    player?.Appellations.Appellations.Contains(Value1) ?? false);

            case UnitReqsKindType.LivingPoint:
                return Ret(SkillResultKeys.skill_urk_living_point,
                    player != null && player.VocationPoint >= Value1 &&
                    (Value2 == 0 || player.VocationPoint <= Value2));

            case UnitReqsKindType.InZone:
                var inZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_in_zone, Value1, inZone?.Id == Value1);

            case UnitReqsKindType.OutZone:
                // Unused
                var outZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_out_zone, Value1, outZone?.Id != Value1);


            case UnitReqsKindType.VerdictOnly:
                return UnsupportedRequirement();

            case UnitReqsKindType.FactionMatchOnly:
                // Is this the same as UnitReqsKindType.FactionMatch ? 
                return RetWithValue(SkillResultKeys.skill_urk_faction_match_only, Value1, (uint)(unit?.Faction?.Id ?? 0) == Value1);

            case UnitReqsKindType.MotherFactionOnly:
                // Is this the same as UnitReqsKindType.MotherFaction ? 
                return Ret(SkillResultKeys.skill_urk_mother_faction_only, (uint)(unit?.Faction?.MotherId ?? 0) == Value1);

            case UnitReqsKindType.FactionMatchOnlyNot:
                return Ret(SkillResultKeys.skill_urk_faction_match_only_not, (uint)(unit?.Faction?.Id ?? 0) != Value1);

            case UnitReqsKindType.MotherFactionOnlyNot:
                return Ret(SkillResultKeys.skill_urk_mother_faction_only_not, (uint)(unit?.Faction?.MotherId ?? 0) != Value1);

            // case UnitReqsKindType.NationMember:
            // case UnitReqsKindType.NationMemberNot:
            // case UnitReqsKindType.DominionMemberAtPos:
            // case UnitReqsKindType.DominionMemberAtPosNot:
            // case UnitReqsKindType.Housing:
            // case UnitReqsKindType.HealthMargin:
            // case UnitReqsKindType.ManaMargin:

            case UnitReqsKindType.LaborPowerMargin:
                // Headroom across BOTH pools: the account cap plus the local cap, minus what is held in
                // either. Both pools are account-wide, so the margin the client shows spans both.
                var remainingLaborMargin = player == null
                    ? 0
                    : TimedRewardsManager.GetMaxLabor(player.PremiumGrade, player.Connection?.Payment?.PremiumState ?? false, player.AccountId) +
                      player.MaxLocalLaborPower -
                      (player.LaborPower + player.LocalLaborPower);
                return RetWithValue(SkillResultKeys.skill_urk_labor_power_margin, Value1, Value1 <= remainingLaborMargin);

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

            case UnitReqsKindType.ExpeditionOwner:
                return Ret(SkillResultKeys.skill_urk_expedition_owner,
                    player != null && player.Expedition?.OwnerId == player.Id);

            case UnitReqsKindType.ExpeditionMember:
                return Ret(SkillResultKeys.skill_urk_expedition_member, player?.Expedition?.Id > 0);

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
                return RetWithValue(SkillResultKeys.skill_urk_less_actability_point, Value1,
                    player != null && player.Actability.GetPoint(Value1, Value3 == 0) < Value2);

            case UnitReqsKindType.OwnQuestItemGroup:
                return Ret(SkillResultKeys.skill_urk_own_quest_item_group,
                    player != null && QuestManager.Instance.GetGroupItems(Value1).Any(itemId =>
                        player.Inventory.GetAllItemsByTemplate(null, itemId, -1, out _, out _)));

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
                return RetWithValue(SkillResultKeys.skill_urk_own_appellation, Value1,
                    player?.Appellations.Appellations.Contains(Value1) ?? false);

            case UnitReqsKindType.EquipAppellation:
                return RetWithValue(SkillResultKeys.skill_urk_equip_appellation, Value1,
                    player?.Appellations.ActiveAppellation == Value1);

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
                return RetWithValue(SkillResultKeys.skill_urk_heir_level, Value1,
                    unit?.HeirLevel >= Value1);

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
                var maximumLabor = player == null
                    ? 0
                    : TimedRewardsManager.GetMaxLabor(player.PremiumGrade, player.Connection?.Payment?.PremiumState ?? false, player.AccountId) +
                      player.MaxLocalLaborPower;
                // Both pools count towards "fully recharged", because maximumLabor is both caps added up.
                return Ret(SkillResultKeys.skill_urk_full_recharged_labor_power,
                    player != null && player.LaborPower + player.LocalLaborPower >= maximumLabor);

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
                var dualUnit = Value1 == 1 ? targetUnit : unit;
                var isDualWielding = dualUnit?.GetWeaponWieldKind() == WeaponWieldKind.DuelWielded;
                return Value2 switch
                {
                    0 => Ret(SkillResultKeys.skill_urk_dual, !isDualWielding),
                    1 => Ret(SkillResultKeys.skill_urk_no_dual, isDualWielding),
                    _ => UnsupportedRequirement()
                };

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

            case UnitReqsKindType.CombatResource:
                if (unit == null)
                    return Ret(SkillResultKeys.skill_invalid_source, false);
                return Ret(SkillResultKeys.skill_urk_combat_resource,
                    (long)unit.GetCombatResource((int)Value1) >= Value2);

            case UnitReqsKindType.TargetManaLessThan:
                return Ret(SkillResultKeys.skill_urk_target_mana_less_than,
                    targetUnit != null && targetUnit.Mpp >= Value1 && targetUnit.Mpp <= Value2);

            case UnitReqsKindType.TargetManaMoreThan:
                return Ret(SkillResultKeys.skill_urk_target_mana_more_than,
                    targetUnit != null && targetUnit.Mpp >= Value1 && targetUnit.Mpp >= Value2);

            case UnitReqsKindType.TargetHealthMoreThan:
                return Ret(SkillResultKeys.skill_urk_target_health_more_than,
                    targetUnit != null && targetUnit.Hpp >= Value1 && targetUnit.Hpp >= Value2);

            case UnitReqsKindType.SourceHealthLessThan:
                return Ret(SkillResultKeys.skill_urk_source_health_less_than,
                    unit != null && unit.Hpp >= Value1 && unit.Hpp <= Value2);

            case UnitReqsKindType.SourceHealthMoreThan:
                return Ret(SkillResultKeys.skill_urk_source_health_more_than,
                    unit != null && unit.Hpp >= Value1 && unit.Hpp >= Value2);

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

            // The Hero Throne in a capital's Hero Hall is the visible one: its sit skill carries a bare
            // Hero requirement (values 0,0,0), so before this it failed with skill_urk_unknown and the
            // chair simply did nothing. The three kinds are the same question asked three ways, so they
            // share one answer rather than drifting apart.
            //
            // Value1 is a hero_grades rank when set - 1 Epherium through 4 Erenor - and 134 of the 153
            // shipped rows leave it 0, meaning any grade. The 19 that set it are the per-grade hero
            // costume pieces and three grade-4 skills. Treated as a minimum rather than an exact match:
            // grades ascend, so the forgiving reading only ever affects whether a higher hero may use a
            // lower grade's item, and refusing that would visibly block someone who outranks the
            // requirement.
            case UnitReqsKindType.Hero:
                if (player == null || !HeroManager.Instance.IsHero(player.Id))
                    return Ret(SkillResultKeys.skill_urk_hero, false);

                // Value1 == 0 asks only "are you a hero", which is already answered. Checking the grade
                // regardless would refuse a hero seated at grade 0, which Grant permits.
                return Ret(SkillResultKeys.skill_urk_hero,
                    Value1 == 0 || HeroManager.Instance.GradeOf(player.Id) >= Value1);

            case UnitReqsKindType.NotHero:
                return Ret(SkillResultKeys.skill_urk_not_hero,
                    player != null && !HeroManager.Instance.IsHero(player.Id));

            // Candidacy is a rank, not a flag - the top hero_conditions.hero_candidate_scope of the
            // nation's ladder. Standing for election and serving are both disqualifying here.
            case UnitReqsKindType.NotHeroNotCandidate:
                return Ret(SkillResultKeys.skill_urk_not_hero,
                    player != null
                    && !HeroManager.Instance.IsHero(player.Id)
                    && !HeroManager.Instance.IsCandidate(player));

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
    }
}
