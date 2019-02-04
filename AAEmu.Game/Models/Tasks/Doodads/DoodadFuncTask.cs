using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public abstract class DoodadFuncTask : Task
    {
        protected Unit _caster;
        protected Doodad _owner;
        protected uint _skillId;

        protected DoodadFuncTask(Unit caster, Doodad owner, uint skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
        }
    }
}
