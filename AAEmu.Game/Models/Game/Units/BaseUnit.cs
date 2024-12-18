using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units;

public class BaseUnit : GameObject, IBaseUnit
{
    public uint Id { get; set; }
    public uint TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SystemFaction Faction { get; set; }
    public SystemFaction OriginFaction { get; set; }

    public virtual float Scale { get; set; } = 1f;

    public IBuffs Buffs { get; set; }
    public SkillModifiers SkillModifiersCache { get; set; }
    public BuffModifiers BuffModifiersCache { get; set; }
    public CombatBuffs CombatBuffs { get; set; }
    public object ChargeLock { get; set; }

    /// <summary>
    /// The loot container for items dropped by this unit
    /// </summary>
    public LootingContainer LootingContainer { get; init; }

    public BaseUnit()
    {
        Buffs = new Buffs(this);
        SkillModifiersCache = new SkillModifiers();
        BuffModifiersCache = new BuffModifiers();
        CombatBuffs = new CombatBuffs(this);
        LootingContainer = new LootingContainer(this);
    }

    /// <summary>
    /// Checks if target can be attacked by checking their factions and combat states
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool CanAttack(BaseUnit target)
    {
        if (this.Faction == null || target.Faction == null)
            return true;
        if (this.ObjId == target.ObjId)
            return false;
        var relation = GetRelationStateTo(target);

        var zone = ZoneManager.Instance.GetZoneByKey(target.Transform.ZoneId);
        var zoneFactionId = zone?.FactionId ?? FactionsEnum.Neutral;
        if (zoneFactionId <= 0)
            zoneFactionId = FactionsEnum.Neutral;
        var zoneFaction = FactionManager.Instance.GetFaction(zoneFactionId);
        if (zoneFaction == null)
        {
            // This is normal behavior for let's say Diamond Shores in 1.2 which is marked with non-existing FactionId 5
            // Logger.Warn($"CanAttack zone faction is null {this.ObjId} - {target.ObjId}");
            zoneFaction = FactionManager.Instance.GetFaction(FactionsEnum.Neutral);
        }
        var targetMotherFaction = target.Faction?.MotherId ?? 0;
        if (this is Character && targetMotherFaction != 0 && ((targetMotherFaction == zoneFaction.MotherId) || (targetMotherFaction == zoneFaction.Id)))
        {
            // Target is protected by mother zone, can't attack it
            return false;
        }

        if (this is Character me && target is Character other)
        {
            var trgIsFlagged = other.Buffs.CheckBuff((uint)BuffConstants.Retribution);

            // Check Safe-zone
            if (other.Faction.MotherId != 0 &&
                other.Faction.MotherId == zoneFactionId
                && !me.IsActivelyHostile(other) &&
                !trgIsFlagged)
            {
                return false;
            }

            var isTeam = TeamManager.Instance.AreTeamMembers(me.Id, other.Id);
            if (trgIsFlagged && !isTeam && relation == RelationState.Friendly)
            {
                return true;
            }
            else if (me.ForceAttack && relation == RelationState.Friendly && !isTeam)
            {
                return true;
            }
        }
        else
        {
            // Handle non-players. Do we need to check target is Npc?

            // Check if npc is protected by safe zone
            // TODO: fix npc safety
            // if (zone.FactionId != 0 && target.Faction.MotherId == zone.FactionId)
            //     return false;
        }

        /*
        // Debug info for player on attacking
        if (this is Character player)
        {
            var targetName = target.Name;
            if (target is Npc npc)
                targetName = "@NPC_NAME(" + npc.TemplateId.ToString() + ")";
            player.SendMessage(ChatType.Shout, $"CanAttack? in Zone:{zoneFaction.Name} => {player.Name} {player.Faction?.Name} => {targetName} ({target.ObjId}) {target.Faction?.Name} = {relation}");
        }
        */

        return relation == RelationState.Hostile;
    }

    /// <summary>
    /// Checks if target should be visible to this Unit by checking stealth
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool CanSeeTarget(BaseUnit target)
    {
        if (!target.IsVisible)
            return false;

        return !target.Buffs.CheckBuffTag((uint)TagsEnum.Stealth);
    }

    public RelationState GetRelationStateTo(BaseUnit unit) => this.Faction?.GetRelationState(unit.Faction) ?? RelationState.Neutral;

    public virtual void AddBonus(uint bonusIndex, Bonus bonus)
    {
    }

    public virtual void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
    {
    }

    public virtual double ApplySkillModifiers(Skill skill, SkillAttribute attribute, double baseValue)
    {
        return SkillModifiersCache.ApplyModifiers(skill, attribute, baseValue);
    }

    public virtual double ApplyBuffModifers(BuffTemplate buff, BuffAttribute attr, double value)
    {
        return BuffModifiersCache.ApplyModifiers(buff, attr, value);
    }

    public virtual void InterruptSkills() { }

    public virtual bool UnitIsVisible(BaseUnit unit)
    {
        if (unit == null)
            return false;

        //Some weird stuff happens here when in an invalid region..
        return Region?.GetNeighbors()?.Any(o => (o?.Id ?? 0) == (unit.Region?.Id ?? 0)) ?? false;
    }

    public override string DebugName()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return base.DebugName();
        return "(" + ObjId.ToString() + ") - " + Name;
    }

    /// <summary>
    /// Get distance between two units taking into account their model sizes
    /// </summary>
    /// <param name="baseUnit"></param>
    /// <param name="includeZAxis"></param>
    /// <returns></returns>
    public float GetDistanceTo(BaseUnit baseUnit, bool includeZAxis = false)
    {
        if (baseUnit == null)
            return 0.0f;

        if (Transform.World.Position.Equals(baseUnit.Transform.World.Position))
            return 0.0f;

        var rawDist = MathUtil.CalculateDistance(Transform.World.Position, baseUnit.Transform.World.Position, includeZAxis);
        if (baseUnit is Shipyard.Shipyard shipyard)
        {
            // Let's use the build radius for this, as it doesn't really have a easy to grab model to get it from 
            rawDist -= ShipyardManager.Instance._shipyardsTemplate[shipyard.ShipyardData.TemplateId].BuildRadius;
        }
        else
        if (baseUnit is House house)
        {
            // Subtract house radius, this should be fair enough for building
            rawDist -= (house.Template.GardenRadius * house.Scale);
        }
        else
        {
            // If target is a Unit, then use it's model for radius
            if (baseUnit is Unit unit)
                rawDist -= ModelManager.Instance.GetActorModel(unit.ModelId)?.Radius ?? 0 * unit.Scale;
        }
        // Subtract own radius
        rawDist -= (this is Unit sourceUnit) ? (ModelManager.Instance.GetActorModel(sourceUnit.ModelId)?.Radius ?? 0) * Scale : 0f;

        return Math.Max(rawDist, 0);
    }
}
