using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public abstract class DoodadFuncTask : Task
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Unit _caster;
        private Doodad _owner;
        private uint _skillId;

        protected DoodadFuncTask(Unit caster, Doodad owner, uint skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
            //_log.Warn("[Doodad] DoodadFuncTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
        }
    }
}
