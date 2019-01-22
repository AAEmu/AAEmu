using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class PlotTask : SkillTask
    {
        private Unit _caster;
        private SkillAction _casterAction;
        private BaseUnit _target;
        private SkillAction _targetAction;
        private PlotNextEvent _nextEvent;

        public PlotTask(Skill skill, Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction,
            PlotNextEvent nextEvent) : base(skill)
        {
            _caster = caster;
            _casterAction = casterAction;
            _target = target;
            _targetAction = targetAction;
            _nextEvent = nextEvent;
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
                if (condition.Condition.Check(_caster, _casterAction, _target, _targetAction))
                    continue;
                step.Flag = 0;
                break;
            }

            var res = true;
            if (step.Flag != 0)
                foreach (var evnt in _nextEvent.Event.NextEvents)
                    res = res && Skill.BuildPlot(_caster, _casterAction, _target, _targetAction, evnt, step);
            Skill.ParsePlot(_caster, _casterAction, _target, _targetAction, step);
            if (!res)
                return;
            TlIdManager.Instance.ReleaseId(Skill.TlId);
            Skill.TlId = 0;
        }
    }
}