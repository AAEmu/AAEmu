using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlot
    {
        public uint Id { get; set; }
        public SkillTargetType TargetTypeId { get; set; }

        public NewPlotEventTemplate FirstEvent { get; set; }

        public void Execute(Unit caster, SkillCaster skillCaster, Unit target, SkillCastTarget skillCastTarget, SkillObject skillObject, Skill skill)
        {
            Console.WriteLine("Starting plot {0}, caster {1}, target {2}", this.Id, caster.Name, target.Name);
            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var plotCaster = new PlotCaster()
            {
                OriginalCaster = caster,
                PreviousCaster = caster
            };
            
            var plotTarget = new PlotTarget()
            {
                PreviousTarget = target,
                OriginalTarget = target
            };

            var callCounter = new Dictionary<uint, int>();
            
            // Run first event
            FirstEvent.Execute(plotCaster, skillCaster, plotTarget, skillCastTarget, skillObject, tlId, skill, callCounter);
            // Send SCPlotEventEnd ?
            caster.BroadcastPacket(new SCPlotEndedPacket(tlId), true);
        }
    }
}
