using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Units;

public class UnitReqs
{
    public uint Id { get; set; }
    public uint OwnerId { get; set; }
    /// <summary>
    /// Possible values: AchievementObjective, AiEvent, ItemArmor, ItemWeapon, QuestComponent, Skill, Sphere
    /// </summary>
    public string OwnerType { get; set; }
    public UnitReqsKindType KindType { get; set; }
    public uint Value1 { get; set; }
    public uint Value2 { get; set; }

    public UnitReqsValidationResult Validate(BaseUnit owner)
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
        var targetUnit = unit?.CurrentTarget as Unit;
        var player = owner as Character;
        switch (KindType)
        {
            case UnitReqsKindType.Level:
                return Ret(SkillResultKeys.skill_urk_level,unit != null && (unit.Level >= Value1 && (Value2 == 0 || unit.Level <= Value2)));

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
                return RetWithValue(SkillResultKeys.skill_urk_own_item, Value1,
                    player != null && player.Inventory.GetAllItemsByTemplate(null, Value1, -1, out _, out _));

            case UnitReqsKindType.TrainedSkill:
                // unused
                return Ret(SkillResultKeys.skill_urk_trained_skill,
                    player?.Skills.Skills.GetValueOrDefault(Value1) != null);

            case UnitReqsKindType.Combat:
                // Only OUTSIDE OF combat
                return Ret(SkillResultKeys.skill_urk_combat, unit is { IsInBattle: false });

            // case UnitReqsKindType.Stealth:
            // case UnitReqsKindType.Health:

            case UnitReqsKindType.Buff:
                return RetWithValue(SkillResultKeys.skill_urk_buff, Value1, unit != null && unit.Buffs.CheckBuff(Value1));

            case UnitReqsKindType.TargetBuff:
                return RetWithValue(SkillResultKeys.skill_urk_target_buff, Value1, targetUnit?.Buffs.CheckBuff(Value1) ?? false);

            case UnitReqsKindType.TargetCombat:
                return Ret(SkillResultKeys.skill_urk_target_combat, targetUnit is { IsInBattle: false });

            // case UnitReqsKindType.CanLearnCraft:
            //     // Learnable crafts is not implemented
            //     return ret(SkillResultKeys.skill_urk_can_learn_craft, player != null && !player.Craft.LearnedCraft(Value1));

            case UnitReqsKindType.DoodadRange:
                if (owner == null)
                    return new UnitReqsValidationResult(SkillResultKeys.skill_urk_doodad_range,0,Value1);
                var rangeCheck = Value2 / 1000f;
                var doodads = WorldManager.GetAround<Doodad>(owner, rangeCheck * 2f, true);
                return RetWithValue(SkillResultKeys.skill_urk_doodad_range, Value1,
                    doodads.Any(doodad => owner.GetDistanceTo(doodad, true) <= rangeCheck && doodad.TemplateId == Value1));

            case UnitReqsKindType.EquipShield:
                // TODO: Validate shield type (value2)
                return Ret(SkillResultKeys.skill_urk_equip_shield, 
                    (unit != null) &&
                    (unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand) is { } item) &&
                    (item.Template is WeaponTemplate weaponTemplate) &&
                    (weaponTemplate.HoldableTemplate.SlotTypeId == (uint)EquipmentItemSlotType.Shield));

            case UnitReqsKindType.NoBuff:
                return RetWithValue(SkillResultKeys.skill_urk_nobuff, Value1, unit != null && !unit.Buffs.CheckBuff(Value1));

            case UnitReqsKindType.TargetBuffTag:
                var targetBuffTarget = targetUnit ?? unit;
                return RetWithValue(SkillResultKeys.skill_urk_target_buff_tag, Value1, targetBuffTarget?.Buffs.CheckBuffTag(Value1) ?? false);

            // case UnitReqsKindType.CorpseRange:
            // case UnitReqsKindType.EquipWeaponType:

            case UnitReqsKindType.TargetHealthLessThan:
                return Ret(SkillResultKeys.skill_urk_target_health_less_than,
                    targetUnit?.Hpp >= Value1 && targetUnit.Hpp <= Value2); 

            case UnitReqsKindType.TargetNpc:
                return RetWithValue(SkillResultKeys.skill_urk_target_npc, Value1,
                    (targetUnit is Npc targetNpc) && (targetNpc.TemplateId == Value1));

            case UnitReqsKindType.TargetDoodad:
                BaseUnit targetDoodad = null;
                if ((unit?.CurrentTarget is Doodad targetDoodadCheck))
                {
                    targetDoodad = targetDoodadCheck;
                }
                else
                {
                    targetDoodad = WorldManager.GetAround<Doodad>(unit, 5f, true)?.Where(x => x.TemplateId == Value1).FirstOrDefault();
                }
                return Ret(SkillResultKeys.skill_urk_target_doodad, (targetDoodad != null) && (targetDoodad.TemplateId == Value1));
            
            case UnitReqsKindType.EquipRanged:
                return Ret(SkillResultKeys.skill_urk_equip_ranged,
                    unit?.Equipment.GetItemBySlot((int)(Value1 == 1
                        ? EquipmentItemSlot.Musical
                        : EquipmentItemSlot.Ranged)) != null);

            case UnitReqsKindType.NoBuffTag:
                return Ret(SkillResultKeys.skill_urk_no_buff_tag, !unit?.Buffs.CheckBuffTag(Value1) ?? false);

            case UnitReqsKindType.CompleteQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_complete_quest_context, Value1, player?.Quests.HasQuestCompleted(Value1) ?? false);

            case UnitReqsKindType.ProgressQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_progress_quest_context, Value1,
                    player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Progress);

            case UnitReqsKindType.ReadyQuestContext:
                return RetWithValue(SkillResultKeys.skill_urk_ready_quest_context, Value1,
                    player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Ready);

            case UnitReqsKindType.TargetNpcGroup:
                // TODO: Find out how to valid NpcGroup (value1)
                return Ret(SkillResultKeys.skill_urk_target_npc_group, (targetUnit is Npc));

            case UnitReqsKindType.AreaSphere:
                // Check Sphere for Quest
                // NOTE: There is an exception for this check in CanUseSkill that handles this separately
                return RetWithValue(SkillResultKeys.skill_urk_area_sphere, Value1, SphereGameData.Instance.IsInsideAreaSphere(Value1, Value2, owner?.Transform?.World?.Position ?? Vector3.Zero) != null);

            case UnitReqsKindType.ExceptCompleteQuestContext:
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, !player?.Quests.HasQuestCompleted(Value1) ?? false);

            case UnitReqsKindType.PreCompleteQuestContext:
                // Not sure if this is correct
                return RetWithValue(SkillResultKeys.skill_urk_precomplete_quest_context, Value1,
                    player != null && !player.Quests.HasQuestCompleted(Value1) &&
                    player.Quests.ActiveQuests.ContainsKey(Value1));

            case UnitReqsKindType.TargetOwnerType:
                // TODO: Not sure if this is supposed the target itself, or it's owner/summoner
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
                    player?.Actability.Actabilities.GetValueOrDefault(Value1)?.Point >= Value2);

            case UnitReqsKindType.CrimePoint:
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, true); //  player?.CrimePoint >= Value1 && player.CrimePoint <= Value2);

            case UnitReqsKindType.HonorPoint:
                return Ret(SkillResultKeys.skill_urk_honor_point,
                    player?.HonorPoint >= Value1 && player.HonorPoint <= Value2);

            case UnitReqsKindType.CrimeRecord:
                // TODO: Verify if CrimeRecord is correct here
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, true); // player?.CrimeRecord >= Value1 && player.CrimeRecord <= Value2);

            case UnitReqsKindType.JuryPoint:
                // TODO: Is this correct? 
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, player?.JuryPoint >= Value1);

            case UnitReqsKindType.SourceOwnerType: 
                // TODO: Not sure if this is supposed the unit itself, or it's owner/summoner
                // No specific type for this?
                return Ret(SkillResultKeys.skill_failure, unit?.BaseUnitType == (BaseUnitType)Value1);
            
            case UnitReqsKindType.Appellation:
                // Unused
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, player?.Appellations.Appellations.Contains(Value1) ?? false);

            case UnitReqsKindType.LivingPoint:
                // Unused
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, player?.VocationPoint >= Value1 && player.VocationPoint <= Value2);

            case UnitReqsKindType.InZone:
                var inZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_in_zone, Value1, inZone?.Id == Value1);
                
            case UnitReqsKindType.OutZone:
                // Unused
                var outZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return RetWithValue(SkillResultKeys.skill_urk_out_zone, Value1, outZone?.Id != Value1);

            // case UnitReqsKindType.DominionOwner: // Needs Castle and Siege implementation

            case UnitReqsKindType.VerdictOnly:
                // This needs implementation of the used to arrest Prime Suspects
                // For now, return always true as the only skill that uses it, also checks for the target buff already
                return Ret(SkillResultKeys.skill_urk_verdict_only, true);

            case UnitReqsKindType.FactionMatchOnly:
                // Is this the same as UnitReqsKindType.FactionMatch ? 
                return RetWithValue(SkillResultKeys.skill_urk_faction_match_only, Value1, (uint)(unit?.Faction?.Id ?? 0) == Value1);
            
            case UnitReqsKindType.MotherFactionOnly:
                // Is this the same as UnitReqsKindType.MotherFaction ? 
                return Ret(SkillResultKeys.skill_urk_mother_faction_only, (uint)(unit?.Faction?.MotherId ?? 0) == Value1);
            
            // case UnitReqsKindType.NationOwner:

            case UnitReqsKindType.FactionMatchOnlyNot:
                return Ret(SkillResultKeys.skill_urk_faction_match_only_not, (uint)(unit?.Faction?.Id ?? 0) != Value1);

            case UnitReqsKindType.MotherFactionOnlyNot:
                return Ret(SkillResultKeys.skill_urk_mother_faction_only_not, (uint)(unit?.Faction?.MotherId ?? 0) != Value1);

            // case UnitReqsKindType.NationMember:
            // case UnitReqsKindType.NationMemberNot:
            // case UnitReqsKindType.NationOwnerAtPos:
            // case UnitReqsKindType.DominionOwnerAtPos:
            // case UnitReqsKindType.Housing:
            // case UnitReqsKindType.HealthMargin:
            // case UnitReqsKindType.ManaMargin:

            case UnitReqsKindType.LaborPowerMargin:
                var remainingLaborMargin =
                    TimedRewardsManager.GetMaxLabor(player?.Connection?.Payment?.PremiumState ?? false) -
                    player?.LaborPower ?? 0;
                return RetWithValue(SkillResultKeys.skill_urk_labor_power_margin, Value1, Value1 <= remainingLaborMargin);

            case UnitReqsKindType.NotOnMovingPhysicalVehicle:
                // Just return true for now
                // This requires checking parents and bindings
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, true); 

            case UnitReqsKindType.MaxLevel:
                return Ret(SkillResultKeys.skill_urk_max_level, player?.Level <= Value1);

            case UnitReqsKindType.ExpeditionOwner:
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, player?.Expedition?.OwnerId == player?.Id);

            case UnitReqsKindType.ExpeditionMember:
                // No specific key for this?
                return Ret(SkillResultKeys.skill_failure, player?.Expedition?.Id > 0);

            case UnitReqsKindType.ExceptProgressQuestContext:
                // No specific key for this?
                var exceptProgressActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return Ret(SkillResultKeys.skill_failure,
                    (!player?.Quests.HasQuestCompleted(Value1) ?? false) && 
                    exceptProgressActiveQuest is not { Step: QuestComponentKind.Progress });

            case UnitReqsKindType.ExceptReadyQuestContext:
                // No specific key for this?
                var exceptReadyActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return Ret(SkillResultKeys.skill_failure,
                    (!player?.Quests.HasQuestCompleted(Value1) ?? false) && 
                    exceptReadyActiveQuest is not { Step: QuestComponentKind.Ready });

            // --- Below is not used in 1.2 ---

            // case UnitReqsKindType.OwnItemNot:
            // case UnitReqsKindType.LessActAbilityPoint:
            // case UnitReqsKindType.OwnQuestItemGroup:
            // case UnitReqsKindType.LeadershipTotal:
            // case UnitReqsKindType.LeadershipCurrent:
            // case UnitReqsKindType.Hero:
            // case UnitReqsKindType.DominionExpeditionMember:
            // case UnitReqsKindType.DominionNationMember:
            // case UnitReqsKindType.OwnItemCount:
            // case UnitReqsKindType.House:
            // case UnitReqsKindType.DoodadTargetFriendly:
            // case UnitReqsKindType.DoodadTargetHostile:
            // case UnitReqsKindType.DominionExpeditionMemberNot:
            // case UnitReqsKindType.DominionMemberNot:
            // case UnitReqsKindType.InZoneGroupHousingExist:
            // case UnitReqsKindType.TargetNoBuffTag:
            // case UnitReqsKindType.ExpeditionLevel:
            // case UnitReqsKindType.IsResident:
            // case UnitReqsKindType.ResidentServicePoint:
            // case UnitReqsKindType.HighAbilityLevel:
            // case UnitReqsKindType.FamilyRole:
            // case UnitReqsKindType.TargetManaLessThan:
            // case UnitReqsKindType.TargetManaMoreThan:
            // case UnitReqsKindType.TargetHealthMoreThan:
            // case UnitReqsKindType.BuffTag:
            // case UnitReqsKindType.LaborPowerMarginLocal:
            default:
                return new UnitReqsValidationResult(SkillResultKeys.ok,0,0);
        }
    }
}
