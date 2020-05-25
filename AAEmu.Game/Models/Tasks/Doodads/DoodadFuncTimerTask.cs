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
            DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, _owner.FuncGroupId, _caster, _owner, _skillId);
        }
    }
}
