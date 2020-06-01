using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncTimerTask : DoodadFuncTask
    {
        private uint _nextPhase;

        public DoodadFuncTimerTask(Unit caster, Doodad owner, uint skillId, uint nextPhase) : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
        }

        public override void Execute()
        {
            _owner.FuncTask = null;
            var phases = DoodadManager.Instance.GetPhaseFunc(_nextPhase);
            if (phases.Length > 0)
            {
                _owner.FuncGroupId = _nextPhase;
                DoodadManager.Instance.TriggerPhases(GetType().Name, _caster, _owner, _skillId);
            }
            else
                DoodadManager.Instance.TriggerFunc(GetType().Name, _caster, _owner, _skillId, _nextPhase);
        }
    }
}
