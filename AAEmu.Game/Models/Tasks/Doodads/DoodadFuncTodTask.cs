using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncTodTask : DoodadFuncTask
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Unit _caster;
        private Doodad _owner;
        private uint _skillId;
        private int _nextPhase;

        public DoodadFuncTodTask(Unit caster, Doodad owner, uint skillId, int nextPhase) : base(caster, owner, skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
            _nextPhase = nextPhase;
        }
        public override void Execute()
        {
            if (_caster is Character)
                _log.Debug("[Doodad] DoodadFuncTodTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
            else
                _log.Trace("[Doodad] DoodadFuncTodTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);

            if (_owner.FuncTask != null)
            {
                _ = _owner.FuncTask.CancelAsync();
                _owner.FuncTask = null;
                if (_caster is Character)
                    _log.Debug("DoodadFuncTodTask: The current timer has been ended.");
                else
                    _log.Trace("DoodadFuncTodTask: The current timer has been ended.");
            }

            _owner.DoPhaseFuncs(_caster, _nextPhase);
        }
    }
}
