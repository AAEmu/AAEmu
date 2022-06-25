using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public abstract class DoodadFuncTask : Task
    {
        protected IUnit _caster;
        protected Doodad _owner;
        protected uint _skillId;

        protected DoodadFuncTask(IUnit caster, Doodad owner, uint skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
        }
    }
}
