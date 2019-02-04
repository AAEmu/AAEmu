using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncGrowthTask : DoodadFuncTask
    {
        private uint _nextPhase;

        public DoodadFuncGrowthTask(Unit caster, Doodad owner, uint skillId, uint nextPhase)
            : base(caster, owner, skillId)
        {
            _nextPhase = nextPhase;
        }

        public override void Execute()
        {
            _owner.FuncTask = null;
            _owner.FuncGroupId = _nextPhase;
            var funcs = DoodadManager.Instance.GetPhaseFunc(_owner.FuncGroupId);
            foreach (var func in funcs)
                func.Use(_caster, _owner, _skillId);
            _owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(_owner), true);
        }
    }
}
