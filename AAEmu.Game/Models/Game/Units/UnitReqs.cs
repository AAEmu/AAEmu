using System.Numerics;
using AAEmu.Game.GameData;
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

    public bool Validate(BaseUnit ownerUnit)
    {
        switch (KindType)
        {
            // case UnitReqsKindType.Level:
            // case UnitReqsKindType.Ability:
            // case UnitReqsKindType.Race:
            // case UnitReqsKindType.Gender:
            // case UnitReqsKindType.EquipSlot:
            // case UnitReqsKindType.EquipItem:
            // case UnitReqsKindType.OwnItem:
            // case UnitReqsKindType.TrainedSkill:
            // case UnitReqsKindType.Combat:
            // case UnitReqsKindType.Stealth:
            // case UnitReqsKindType.Health:
            // case UnitReqsKindType.Buff:
            // case UnitReqsKindType.TargetBuff:
            // case UnitReqsKindType.TargetCombat:
            // case UnitReqsKindType.CanLearnCraft:
            // case UnitReqsKindType.DoodadRange:
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
                return SphereGameData.Instance.IsInsideAreaSphere(Value1, Value2, ownerUnit?.Transform?.World?.Position ?? Vector3.Zero);
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
