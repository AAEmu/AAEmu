using System.Collections.Generic;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class PlotTask : SkillTask
    {
        private readonly Unit _caster;
        private readonly SkillCaster _casterCaster;
        private readonly BaseUnit _target;
        private readonly SkillCastTarget _targetCaster;
        private readonly PlotNextEvent _nextEvent;
        private readonly SkillObject _skillObject;
        private readonly Dictionary<uint, int> _counter;

        public PlotTask(Skill skill, Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
            SkillObject skillObject, PlotNextEvent nextEvent, Dictionary<uint, int> counter) : base(skill)
        {
            _caster = caster;
            _casterCaster = casterCaster;
            _target = target;
            _targetCaster = targetCaster;
            _skillObject = skillObject;
            _nextEvent = nextEvent;
            _counter = counter;
        }

        public override void Execute()
        {
            _caster.SkillTask = null;
            var step = new PlotStep();
            step.Event = _nextEvent.Event;
            step.Casting = _nextEvent.Casting;
            step.Channeling = _nextEvent.Channeling;
            step.Flag = 2;
            foreach (var condition in _nextEvent.Event.Conditions)
            {
                if (condition.Condition.Check(_caster, _casterCaster, _target, _targetCaster, _skillObject, condition))
                    continue;
                step.Flag = 0;
                break;
            }

            var res = true;
            // if (step.Flag != 0)
            //     foreach (var evnt in _nextEvent.Event.NextEvents)
            //         res = res && Skill.BuildPlot(_caster, _casterCaster, _target, _targetCaster, _skillObject, evnt,
            //                   step, _counter);
            // Skill.ParsePlot(_caster, _casterCaster, _target, _targetCaster, _skillObject, step);
            if (!res)
                return;
            _caster.BroadcastPacket(new SCPlotEndedPacket(Skill.TlId), true);
            TlIdManager.Instance.ReleaseId(Skill.TlId);
            Skill.TlId = 0;
        }
    }
}
