using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotCondition
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public bool NotCondition { get; set; }
        public PlotConditionType Kind { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
        public int Param3 { get; set; }

        public bool Check(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var res = true;
            switch (Kind)
            {
                case PlotConditionType.Level:
                    // This one is complicated ay
                    res = true;
                    break;
                case PlotConditionType.Relation:
                    res = true;
                    break;
                case PlotConditionType.Direction:
                    res = true;
                    break;
                case PlotConditionType.BuffTag:
                    res = target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)Param1));
                    break;
                case PlotConditionType.WeaponEquipStatus:
                    res = true; 
                    break;
                case PlotConditionType.Chance:
                    var chance = Rand.Next(0, 100);
                    res = chance <= Param1;
                    break;
                case PlotConditionType.Dead:
                    var unitTarget = (Unit)target;
                    res = unitTarget.Hp == 0;
                    break;
                case PlotConditionType.CombatDiceResult:
                    res = false; // Every CombatDiceResult is a NotCondition -> false makes it true. 
                    break;
                case PlotConditionType.InstrumentType:
                    res = true;
                    break;
                case PlotConditionType.Range:
                    var range = MathUtil.CalculateDistance(caster.Position, target.Position);
                    res = range >= Param1 && range <= Param2;
                    break;
                case PlotConditionType.Variable:
                    res = true;
                    break;
                case PlotConditionType.UnitAttrib:
                    res = true;
                    break;
                case PlotConditionType.Actability:
                    res = true;
                    break;
                case PlotConditionType.Stealth:
                    // unsure if player or target
                    // only used for Flamebolt for some reason.
                    // Also always a "NotCondition" so will default to false (result will be True)
                    res = false;
                    break;
                case PlotConditionType.Visible:
                    // used for LOS ?
                    res = true;
                    break;
                case PlotConditionType.ABLevel:
                    var level = caster.Level;
                    // Unsure what Param1 is. Seems like a 3 bit flag
                    // For Arc Lightning, we have a value of 7, using 3 different conditions
                    // Need to find what the flags mean
                    // Could be target/caster level ? 
                    // 1 = caster
                    // 2 = target
                    // 4 = both ? idk 
                    res = level >= Param2 && level <= Param3;
                    break;
            }

            _log.Debug("PlotCondition : {0} | Params : {1}, {2}, {3} | Result : {4}", Kind, Param1, Param2, Param3, NotCondition ? !res : res);            

            return NotCondition ? !res : res;
        }
    }
}
