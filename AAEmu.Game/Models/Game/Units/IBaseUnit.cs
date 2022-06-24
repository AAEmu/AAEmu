using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IBaseUnit : IGameObject
    {
        BuffModifiers BuffModifiersCache { get; set; }
        Buffs Buffs { get; set; }
        CombatBuffs CombatBuffs { get; set; }
        SystemFaction Faction { get; set; }
        string Name { get; set; }
        float Scale { get; set; }
        SkillModifiers SkillModifiersCache { get; set; }

        void AddBonus(uint bonusIndex, Bonus bonus);
        double ApplyBuffModifers(BuffTemplate buff, BuffAttribute attr, double value);
        double ApplySkillModifiers(Skill skill, SkillAttribute attribute, double baseValue);
        bool CanAttack(IBaseUnit target);
        RelationState GetRelationStateTo(IBaseUnit unit);
        void InterruptSkills();
        void RemoveBonus(uint bonusIndex, UnitAttribute attribute);
        bool UnitIsVisible(IBaseUnit unit);
    }
}
