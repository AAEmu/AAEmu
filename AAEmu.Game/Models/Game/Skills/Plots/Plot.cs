using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Packets.G2C;

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
            caster.BroadcastPacket(new SCSkillStartedPacket(skill.Id, skill.TlId, casterCaster, targetCaster, skill, skillObject), true);
            await EventTemplate.PlayEvent(instance, null);
            return true;
        }
    }
}
