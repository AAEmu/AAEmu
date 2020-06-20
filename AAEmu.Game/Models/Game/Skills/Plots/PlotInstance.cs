using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotInstance
    {
        public PlotNextEvent CurrentNextEvent { get; set; }

        public Dictionary<uint, int> Tickets { get; set; }
        public PlotConditionsCache ConditionsCache { get; set; }
        public List<int> Variables { get; set; }
        public byte CombatDiceRoll { get; set; }

        public Skill ActiveSkill { get; set; }

        public Unit Caster { get; set; }
        public SkillCaster CasterCaster { get; set; }
        public BaseUnit Target { get; set; }
        public SkillCastTarget TargetCaster { get; set; }
        public SkillObject SkillObject { get; set; }

        public byte Flag { get; set; }


        public PlotInstance(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Skill skill)
        {
            Tickets = new Dictionary<uint, int>();
            ConditionsCache = new PlotConditionsCache();
            Variables = new List<int>();

            Caster = caster;
            CasterCaster = casterCaster;
            Target = target;
            TargetCaster = targetCaster;
            SkillObject = skillObject;

            ActiveSkill = skill;
        }
        public bool UseConditionCache(PlotCondition condition)
        {
            switch (condition.Kind)
            {
                case PlotConditionType.BuffTag:
                    return ConditionsCache.BuffTagCache.ContainsKey(condition.Param1);
            }
            return false;
        }
        public bool GetConditionCacheResult(PlotCondition condition)
        {
            switch (condition.Kind)
            {
                case PlotConditionType.BuffTag:
                    return ConditionsCache.BuffTagCache[condition.Param1];
            }

            
            return false;
        }
        public void UpdateConditionCache(PlotCondition condition, bool result)
        {
            switch (condition.Kind)
            {
                case PlotConditionType.BuffTag:
                    ConditionsCache.BuffTagCache.TryAdd(condition.Param1, result);
                break;
            }
        }
    }
}
