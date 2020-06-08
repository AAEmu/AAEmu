using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
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

    public class BaseUnit : GameObject
    {
        public string Name { get; set; } = string.Empty;
        public SystemFaction Faction { get; set; }

        public virtual float Scale => 1f;

        public Effects Effects { get; set; }
        public SkillModifiers Modifiers { get; set; }

        public BaseUnit()
        {
            Effects = new Effects(this);
            Modifiers = new SkillModifiers();
        }

        public virtual void AddBonus(uint bonusIndex, Bonus bonus)
        {
        }

        public virtual void RemoveBonus(uint bonusIndex, UnitAttribute attribute)
        {
        }

        public virtual double ApplySkillModifiers(Skill skill, SkillAttribute attribute, double baseValue)
        {
            return Modifiers.ApplyModifiers(skill, attribute, baseValue);
        }

        public virtual SkillTargetRelation GetRelationTo(BaseUnit other)
        {
            if (Faction.Id == other.Faction.Id)
                return SkillTargetRelation.Friendly;

            var relation = other.Faction.GetRelationState(Faction.Id);
            switch (relation)
            {
                case RelationState.Friendly:
                    return SkillTargetRelation.Friendly;
                case RelationState.Hostile:
                    return SkillTargetRelation.Hostile;
                case RelationState.Neutral:
                    return SkillTargetRelation.Others;
                default:
                    return SkillTargetRelation.Others;
            }
        }
    }
}
