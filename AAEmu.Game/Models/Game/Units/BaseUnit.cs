using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public enum BaseUnitType : byte
    {
        Character = 0,
        Npc = 1,
        Slave = 2,
        Housing = 3,
        Transfer = 4,
        Mate = 5,
        Shipyard = 6
    }

    public enum ModelPostureType : byte
    {
        None = 0,
        HouseState = 1,
        ActorModelState = 4,
        FarmfieldState = 7,
        TurretState = 8
    }

    public class BaseUnit : GameObject, IBaseUnit
    {
        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public SystemFaction Faction { get; set; }

        public virtual float Scale { get; set; } = 1f;
        
        public IBuffs Buffs { get; set; }
        public SkillModifiers SkillModifiersCache { get; set; }
        public BuffModifiers BuffModifiersCache { get; set; }
        public CombatBuffs CombatBuffs { get; set; }

        public BaseUnit()
        {
            Buffs = new Buffs(this);
            SkillModifiersCache = new SkillModifiers();
            BuffModifiersCache = new BuffModifiers();
            CombatBuffs = new CombatBuffs(this);
        }

        public bool CanAttack(BaseUnit target)
        {
            if (this.Faction == null || target.Faction == null)
                return false;
            if (this.ObjId == target.ObjId)
                return false;
            var relation = GetRelationStateTo(target);
            var zone = ZoneManager.Instance.GetZoneByKey(target.Transform.ZoneId);
            if (this is Character me && target is Character other)
            {
                var trgIsFlagged = other.Buffs.CheckBuff((uint)BuffConstants.Retribution);

                //check safezone
                if (other.Faction.MotherId != 0 && other.Faction.MotherId == zone.FactionId 
                    && !me.IsActivelyHostile(other) && !trgIsFlagged)
                {
                    return false;
                }

                bool isTeam = TeamManager.Instance.AreTeamMembers(me.Id, other.Id);
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
                //handle non-players. Do we need to check target is Npc?

                //Check if npc is protected by safe zone
                //TODO fix npc safety
                //if (zone.FactionId != 0 && target.Faction.MotherId == zone.FactionId)
                    //return false;
            }
            

            return relation == RelationState.Hostile;
        }

        public RelationState GetRelationStateTo(BaseUnit unit) => this.Faction?.GetRelationState(unit.Faction) ?? RelationState.Neutral;

        public virtual void AddBonus(uint bonusIndex, Bonus bonus)
        {
        }

        public virtual void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
        {
        }
        
        public virtual double ApplySkillModifiers(Skill skill, SkillAttribute attribute, double baseValue) {
            return SkillModifiersCache.ApplyModifiers(skill, attribute, baseValue);
        }

        public virtual double ApplyBuffModifers(BuffTemplate buff, BuffAttribute attr, double value)
        {
            return BuffModifiersCache.ApplyModifiers(buff, attr, value);
        }

        public virtual void InterruptSkills() {}

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
    }
}
