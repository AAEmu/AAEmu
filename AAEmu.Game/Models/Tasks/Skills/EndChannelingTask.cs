using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class EndChannelingTask : SkillTask
    {
        private readonly Unit _caster;
        private readonly SkillCaster _casterCaster;
        private readonly BaseUnit _target;
        private readonly SkillCastTarget _targetCaster;
        private readonly SkillObject _skillObject;
        public Doodad _channelDoodad { get; set; }

        public EndChannelingTask(Skill skill, Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Doodad channelDoodad) : base(skill)
        {
            _caster = caster;
            _casterCaster = casterCaster;
            _target = target;
            _targetCaster = targetCaster;
            _skillObject = skillObject;
            _channelDoodad = channelDoodad;
        }

        public override void Execute()
        {
            // Skill.ScheduleEffects(_caster, _casterCaster, _target, _targetCaster, _skillObject);
            Skill.EndChanneling(_caster, _channelDoodad);
        }
    }
}
