using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncGrowthTask : DoodadFuncTask
    {
        private int _nextPhase;

        public DoodadFuncGrowthTask(Unit caster, Doodad owner, uint skillId, int nextPhase) : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
        }

        public override void Execute()
        {
            _owner.GoToPhase(_caster, _nextPhase);
        }
    }
}
