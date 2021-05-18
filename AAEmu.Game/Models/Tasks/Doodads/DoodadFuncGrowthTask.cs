using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncGrowthTask : DoodadFuncTask
    {
        private int _nextPhase;
        private float _endScale;

        public DoodadFuncGrowthTask(Unit caster, Doodad owner, uint skillId, int nextPhase, float endScale) : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
            _endScale = endScale;
        }

        public override void Execute()
        {
            _owner.Scale = _endScale;
            _owner.GoToPhase(_caster, _nextPhase);
        }
    }
}
