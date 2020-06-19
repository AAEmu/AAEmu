using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class Plot
    {
        public uint Id { get; set; }
        public uint TargetTypeId { get; set; }

        public PlotEventTemplate EventTemplate { get; set; }

        public async Task<bool> Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Skill skill ,CancellationToken token)
        {
            PlotInstance instance = new PlotInstance(caster, casterCaster, target, targetCaster, skillObject, skill);
            await EventTemplate.PlayEvent(instance);
            return true;
        }
    }
}
