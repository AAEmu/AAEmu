using System.Collections.Generic;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
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

    public bool Validate(BaseUnit owner)
    {
        var unit = owner as Unit;
        var player = owner as Character;
        switch (KindType)
        {
            case UnitReqsKindType.Level:
                return unit != null && (unit.Level >= Value1 && unit.Level <= Value2);
            case UnitReqsKindType.Ability:
                return player != null && player.Abilities.GetAbilityLevel((AbilityType)Value1) >= Value2;
            case UnitReqsKindType.Race:
                return player != null && player.Race == (Race)Value1;
            case UnitReqsKindType.Gender:
                return player != null && player.Gender == (Gender)Value1;
            case UnitReqsKindType.EquipSlot:
                return unit != null && unit.Equipment.GetItemBySlot((int)Value1) != null;
            case UnitReqsKindType.EquipItem:
                return unit != null && unit.Equipment.GetAllItemsByTemplate(Value1, -1, out _, out _);
            case UnitReqsKindType.OwnItem:
                return player != null && player.Inventory.GetAllItemsByTemplate(null, Value1, -1, out _, out _);
            case UnitReqsKindType.TrainedSkill:
                // unused
                return player != null && player.Skills.Skills.GetValueOrDefault(Value1) != null;
            case UnitReqsKindType.Combat:
                // Only OUTSIDE OF combat
                return unit != null && !unit.IsInBattle;
            // case UnitReqsKindType.Stealth:
            // case UnitReqsKindType.Health:
            case UnitReqsKindType.Buff:
                return unit != null && unit.Buffs.CheckBuff(Value1);
            case UnitReqsKindType.TargetBuff:
                return unit != null && unit.Buffs.CheckBuffTag(Value1);
            case UnitReqsKindType.TargetCombat:
                return (unit?.CurrentTarget is Unit { IsInBattle: true });
            // case UnitReqsKindType.CanLearnCraft:
            //     // Learnable crafts is not implemented
            //     return player != null && !player.Craft.LearnedCraft(Value1);
            case UnitReqsKindType.DoodadRange:
                if (owner == null)
                    return false;
                var rangeCheck = Value2 / 1000f;
                var doodads = WorldManager.GetAround<Doodad>(owner, rangeCheck * 2f, true);
                foreach (var doodad in doodads)
                {
                    
                }

                return false;
            // case UnitReqsKindType.EquipShield:
            // case UnitReqsKindType.NoBuff:
            // case UnitReqsKindType.TargetBuffTag:
            // case UnitReqsKindType.CorpseRange:
            // case UnitReqsKindType.EquipWeaponType:
            // case UnitReqsKindType.TargetHealthLessThan:
            // case UnitReqsKindType.TargetNpc:
            // case UnitReqsKindType.TargetDoodad:
            // case UnitReqsKindType.EquipRanged:
            // case UnitReqsKindType.NoBuffTag:
            // case UnitReqsKindType.CompleteQuestContext:
            // case UnitReqsKindType.ProgressQuestContext:
            // case UnitReqsKindType.ReadyQuestContext:
            // case UnitReqsKindType.TargetNpcGroup:
            case UnitReqsKindType.AreaSphere:
                // Check Sphere for Quest
                return SphereGameData.Instance.IsInsideAreaSphere(Value1, Value2, owner?.Transform?.World?.Position ?? Vector3.Zero);
            // case UnitReqsKindType.ExceptCompleteQuestContext:
            // case UnitReqsKindType.PreCompleteQuestContext:
            // case UnitReqsKindType.TargetOwnerType:
            // case UnitReqsKindType.NotUnderWater:
            // case UnitReqsKindType.FactionMatch:
            // case UnitReqsKindType.Tod:
            // case UnitReqsKindType.MotherFaction:
            // case UnitReqsKindType.ActAbilityPoint:
            // case UnitReqsKindType.CrimePoint:
            // case UnitReqsKindType.HonorPoint:
            // case UnitReqsKindType.CrimeRecord:
            // case UnitReqsKindType.JuryPoint:
            // case UnitReqsKindType.SourceOwnerType:
            // case UnitReqsKindType.Appellation:
            // case UnitReqsKindType.LivingPoint:
            // case UnitReqsKindType.InZone:
            // case UnitReqsKindType.OutZone:
            // case UnitReqsKindType.DominionOwner:
            // case UnitReqsKindType.VerdictOnly:
            // case UnitReqsKindType.FactionMatchOnly:
            // case UnitReqsKindType.MotherFactionOnly:
            // case UnitReqsKindType.NationOwner:
            // case UnitReqsKindType.FactionMatchOnlyNot:
            // case UnitReqsKindType.MotherFactionOnlyNot:
            // case UnitReqsKindType.NationMember:
            // case UnitReqsKindType.NationMemberNot:
            // case UnitReqsKindType.NationOwnerAtPos:
            // case UnitReqsKindType.DominionOwnerAtPos:
            // case UnitReqsKindType.Housing:
            // case UnitReqsKindType.HealthMargin:
            // case UnitReqsKindType.ManaMargin:
            // case UnitReqsKindType.LaborPowerMargin:
            // case UnitReqsKindType.NotOnMovingPhysicalVehicle:
            // case UnitReqsKindType.MaxLevel:
            // case UnitReqsKindType.ExpeditionOwner:
            // case UnitReqsKindType.ExpeditionMember:
            // case UnitReqsKindType.ExceptProgressQuestContext:
            // case UnitReqsKindType.ExceptReadyQuestContext:
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
