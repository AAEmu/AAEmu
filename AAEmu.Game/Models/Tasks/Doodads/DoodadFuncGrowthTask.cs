using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncGrowthTask : DoodadFuncTask
    {
        private uint _nextPhase;

        private static Logger _log = LogManager.GetCurrentClassLogger();

        public DoodadFuncGrowthTask(Unit caster, Doodad owner, uint skillId, uint nextPhase)
            : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
        }

        public override void Execute()
        {
            _log.Debug(_owner.FuncGroupId + ": triggered a growth task");
            _owner.FuncTask = null;
            _owner.FuncGroupId = _nextPhase;
            DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, _owner.FuncGroupId, _caster, _owner, _skillId);
        }
    }
}
