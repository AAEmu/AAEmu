using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncGrowthTask : DoodadFuncTask
    {
        private uint _nextPhase;

        public DoodadFuncGrowthTask(Unit caster, Doodad owner, uint skillId, uint nextPhase) : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
        }

        public override void Execute()
        {
            _owner.FuncGroupId = _nextPhase;
            _owner.FuncTask = null;
            DoodadManager.Instance.TriggerPhases(GetType().Name, _caster, _owner, _skillId);
        }
    }
}
