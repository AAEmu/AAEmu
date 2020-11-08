using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotState
    {
        public Dictionary<uint, int> Tickets { get; set; }
        public List<int> Variables { get; set; }
        public byte CombatDiceRoll { get; set; }

        public Skill ActiveSkill { get; set; }
        public Unit Caster { get; set; }
        public SkillCaster CasterCaster { get; set; }
        public BaseUnit Target { get; set; }
        public SkillCastTarget TargetCaster { get; set; }
        public SkillObject SkillObject { get; set; }

        public List<GameObject> HitObjects { get; set; }

        public PlotState(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Skill skill)
        {
            Caster = caster;
            CasterCaster = casterCaster;
            Target = target;
            TargetCaster = targetCaster;
            SkillObject = skillObject;
            ActiveSkill = skill;
            
            HitObjects = new List<GameObject>();
            Tickets = new Dictionary<uint, int>();
            Variables = new List<int>();
        }
        public PlotState() //for testing only
        {
            HitObjects = new List<GameObject>();
            Tickets = new Dictionary<uint, int>();
            Variables = new List<int>();
        }
    }
}
