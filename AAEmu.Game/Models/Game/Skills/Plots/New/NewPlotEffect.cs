using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotEffect
    {
        public NewPlotEventTemplate Event { get; set; }
        public uint Position { get; set; }
        public PlotEffectSource Source { get; set; }
        public PlotEffectSource Target { get; set; }
        public uint EffectId { get; set; }
        public string EffectType { get; set; }

        public void Apply(Unit caster, SkillCaster skillCaster, Unit target, SkillCastTarget skillCastTarget, SkillObject skillObject, ushort tlId, Skill skill)
        {
            var template = SkillManager.Instance.GetEffectTemplate(EffectId, EffectType);
            template?.Apply(caster, skillCaster, target, skillCastTarget, new CastPlot(Event.Plot.Id, tlId, Event.Id, skill.Template.Id), skill, skillObject, DateTime.Now);
        }
    }
}
