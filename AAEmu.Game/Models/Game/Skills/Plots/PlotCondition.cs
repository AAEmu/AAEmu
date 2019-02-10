using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotCondition
    {
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
                case PlotConditionType.Distance:
                    //var dist = MathUtil.CalculateDistance(caster.Position, target.Position);
                    var dist = 10;
                    res = dist >= Param1 && dist <= Param2;
                    break;
                case PlotConditionType.BuffTag:
                    res = target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)Param1));
                    break;
                case PlotConditionType.Unk6:
                    res = false; //TODO
                    break;
                case PlotConditionType.Unk7:
                    res = false; //TODO
                    break;
            }

            return NotCondition ? !res : res;
        }
    }
}
