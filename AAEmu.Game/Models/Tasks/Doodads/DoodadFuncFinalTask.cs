using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncFinalTask : DoodadFuncTask
    {
        private bool _respawn;

        public DoodadFuncFinalTask(Unit caster, Doodad owner, uint skillId, bool respawn) : base(caster, owner, skillId)
        {
            _respawn = respawn;
        }

        public override void Execute()
        {
            _owner.FuncTask = null;
            // TODO respawn...
            _owner.Delete();
        }
    }
}
