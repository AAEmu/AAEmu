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
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;

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

    public bool Validate(BaseUnit owner)
    {
        var unit = owner as Unit;
        var targetUnit = unit?.CurrentTarget as Unit;
        var player = owner as Character;
        switch (KindType)
        {
            case UnitReqsKindType.Level:
                return unit != null && (unit.Level >= Value1 && (Value2 == 0 || unit.Level <= Value2));

            case UnitReqsKindType.Ability:
                return player != null && player.Abilities.GetAbilityLevel((AbilityType)Value1) >= Value2;

            case UnitReqsKindType.Race:
                return player != null && player.Race == (Race)Value1;

            case UnitReqsKindType.Gender:
                return player != null && player.Gender == (Gender)Value1;

            case UnitReqsKindType.EquipSlot:
                return unit?.Equipment.GetItemBySlot((int)Value1) != null;

            case UnitReqsKindType.EquipItem:
                return unit != null && unit.Equipment.GetAllItemsByTemplate(Value1, -1, out _, out _);

            case UnitReqsKindType.OwnItem:
                return player != null && player.Inventory.GetAllItemsByTemplate(null, Value1, -1, out _, out _);

            case UnitReqsKindType.TrainedSkill:
                // unused
                return player?.Skills.Skills.GetValueOrDefault(Value1) != null;

            case UnitReqsKindType.Combat:
                // Only OUTSIDE OF combat
                return unit is { IsInBattle: false };

            // case UnitReqsKindType.Stealth:
            // case UnitReqsKindType.Health:

            case UnitReqsKindType.Buff:
                return unit != null && unit.Buffs.CheckBuff(Value1);

            case UnitReqsKindType.TargetBuff:
                return targetUnit?.Buffs.CheckBuff(Value1) ?? false;

            case UnitReqsKindType.TargetCombat:
                return targetUnit is { IsInBattle: true };

            // case UnitReqsKindType.CanLearnCraft:
            //     // Learnable crafts is not implemented
            //     return player != null && !player.Craft.LearnedCraft(Value1);

            case UnitReqsKindType.DoodadRange:
                if (owner == null)
                    return false;
                var rangeCheck = Value2 / 1000f;
                var doodads = WorldManager.GetAround<Doodad>(owner, rangeCheck * 2f, true);
                return doodads.Any(doodad => owner.GetDistanceTo(doodad, true) <= rangeCheck);

            case UnitReqsKindType.EquipShield:
                // TODO: Validate shield type (value2)
                return (unit != null) &&
                       (unit.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand) is Item item) &&
                       (item.Template is WeaponTemplate weaponTemplate) &&
                       (weaponTemplate.HoldableTemplate.SlotTypeId == (uint)EquipmentItemSlotType.Shield);

            case UnitReqsKindType.NoBuff:
                return unit != null && !unit.Buffs.CheckBuff(Value1);

            case UnitReqsKindType.TargetBuffTag:
                return targetUnit?.Buffs.CheckBuffTag(Value1) ?? false;

            // case UnitReqsKindType.CorpseRange:
            // case UnitReqsKindType.EquipWeaponType:

            case UnitReqsKindType.TargetHealthLessThan:
                return targetUnit?.Hpp >= Value1 && targetUnit?.Hpp <= Value2; 

            case UnitReqsKindType.TargetNpc:
                return (targetUnit is Npc targetNpc) && (targetNpc.TemplateId == Value1);

            case UnitReqsKindType.TargetDoodad:
                return (unit?.CurrentTarget is Doodad targetDoodad) && (targetDoodad.TemplateId == Value1);
            
            case UnitReqsKindType.EquipRanged:
                return unit?.Equipment.GetItemBySlot((int)(Value1 == 1 ? EquipmentItemSlot.Musical : EquipmentItemSlot.Ranged)) != null;

            case UnitReqsKindType.NoBuffTag:
                return !unit?.Buffs.CheckBuffTag(Value1) ?? false;

            case UnitReqsKindType.CompleteQuestContext:
                return player?.Quests.HasQuestCompleted(Value1) ?? false;

            case UnitReqsKindType.ProgressQuestContext:
                return player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Progress;

            case UnitReqsKindType.ReadyQuestContext:
                return player?.Quests.ActiveQuests.GetValueOrDefault(Value1)?.Step == QuestComponentKind.Ready;

            case UnitReqsKindType.TargetNpcGroup:
                // TODO: Find out how to valid NpcGroup (value1)
                return (targetUnit is Npc);

            case UnitReqsKindType.AreaSphere:
                // Check Sphere for Quest
                return SphereGameData.Instance.IsInsideAreaSphere(Value1, Value2, owner?.Transform?.World?.Position ?? Vector3.Zero);

            case UnitReqsKindType.ExceptCompleteQuestContext:
                return !player?.Quests.HasQuestCompleted(Value1) ?? false;

            case UnitReqsKindType.PreCompleteQuestContext:
                // Not sure if this is correct
                return player != null && !player.Quests.HasQuestCompleted(Value1) && player.Quests.ActiveQuests.ContainsKey(Value1);

            case UnitReqsKindType.TargetOwnerType:
                // TODO: Not sure if this is supposed the target itself, or it's owner/summoner
                return targetUnit?.BaseUnitType == (BaseUnitType)Value1;

            case UnitReqsKindType.NotUnderWater:
                return !unit?.IsUnderWater ?? false;

            case UnitReqsKindType.FactionMatch:
                return unit?.Faction?.Id == Value1;

            case UnitReqsKindType.Tod:
                var currentTime = (uint)Math.Floor(TimeManager.Instance.GetTime() * 100f);
                return currentTime >= Value1 && currentTime <= Value2;

            case UnitReqsKindType.MotherFaction:
                return unit?.Faction.MotherId == Value1;

            case UnitReqsKindType.ActAbilityPoint:
                return player?.Actability.Actabilities.GetValueOrDefault(Value1)?.Point >= Value2;

            case UnitReqsKindType.CrimePoint:
                return player?.CrimePoint >= Value1 && player.CrimePoint <= Value2;

            case UnitReqsKindType.HonorPoint:
                return player?.HonorPoint >= Value1 && player.HonorPoint <= Value2;

            case UnitReqsKindType.CrimeRecord:
                // TODO: Verify if CrimeRecord is correct here
                return player?.CrimeRecord >= Value1 && player.CrimeRecord <= Value2;

            case UnitReqsKindType.JuryPoint:
                // TODO: Is this correct? 
                return player?.JuryPoint >= Value1;

            case UnitReqsKindType.SourceOwnerType: 
                // TODO: Not sure if this is supposed the unit itself, or it's owner/summoner
                return unit?.BaseUnitType == (BaseUnitType)Value1;
            
            case UnitReqsKindType.Appellation:
                // Unused
                return player?.Appellations.Appellations.Contains(Value1) ?? false;

            case UnitReqsKindType.LivingPoint:
                // Unused
                return player?.VocationPoint >= Value1 && player.VocationPoint <= Value2;

            case UnitReqsKindType.InZone:
                var inZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return inZone?.Id == Value1;
                
            case UnitReqsKindType.OutZone:
                // Unused
                var outZone = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
                return outZone?.Id != Value1;

            // case UnitReqsKindType.DominionOwner: // Needs Castle and Siege implementation

            case UnitReqsKindType.VerdictOnly:
                // This needs implementation of the used to arrest Prime Suspects
                // For now, return always true as the only skill that uses it, also checks for the target buff already
                return true;

            case UnitReqsKindType.FactionMatchOnly:
                // Is this the same as UnitReqsKindType.FactionMatch ? 
                return unit?.Faction?.Id == Value1;
            
            case UnitReqsKindType.MotherFactionOnly:
                // Is this the same as UnitReqsKindType.MotherFaction ? 
                return unit?.Faction?.MotherId == Value1;
            
            // case UnitReqsKindType.NationOwner:

            case UnitReqsKindType.FactionMatchOnlyNot:
                return unit?.Faction?.Id != Value1;

            case UnitReqsKindType.MotherFactionOnlyNot:
                return unit?.Faction?.MotherId != Value1;

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
                return Value1 <= remainingLaborMargin;

            case UnitReqsKindType.NotOnMovingPhysicalVehicle:
                // Just return true for now
                // This requires checking parents and bindings
                return true; 

            case UnitReqsKindType.MaxLevel:
                return player?.Level <= Value1;

            case UnitReqsKindType.ExpeditionOwner:
                return player?.Expedition?.OwnerId == player?.Id;

            case UnitReqsKindType.ExpeditionMember:
                return player?.Expedition?.Id > 0;

            case UnitReqsKindType.ExceptProgressQuestContext:
                var exceptProgressActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return (!player?.Quests.HasQuestCompleted(Value1) ?? false) && exceptProgressActiveQuest is not { Step: QuestComponentKind.Progress };

            case UnitReqsKindType.ExceptReadyQuestContext:
                var exceptReadyActiveQuest = player?.Quests.ActiveQuests.GetValueOrDefault(Value1);
                return (!player?.Quests.HasQuestCompleted(Value1) ?? false) && exceptReadyActiveQuest is not { Step: QuestComponentKind.Ready };

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
                return true;
        }
    }
}
