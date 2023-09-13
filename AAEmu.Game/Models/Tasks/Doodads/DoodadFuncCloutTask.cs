using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads
{
    public class DoodadFuncCloutTask : DoodadFuncTask
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private BaseUnit _caster;
        private Doodad _owner;
        private uint _skillId;
        private int _nextPhase;
        private AreaTrigger _araAreaTrigger;

        public DoodadFuncCloutTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase, AreaTrigger araAreaTrigger) : base(caster, owner, skillId)
        {
            _caster = caster;
            _owner = owner;
            _skillId = skillId;
            _nextPhase = nextPhase;
            _araAreaTrigger = araAreaTrigger;
        }
        public override void Execute()
        {
            if (_caster is Character)
                _log.Debug("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);
            else
                _log.Trace("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);

            _owner.FuncTask = null;

            if (_nextPhase == -1)
                _owner.Delete();

            AreaTriggerManager.Instance.RemoveAreaTrigger(_araAreaTrigger);
            _owner.DoChangePhase(_caster, _nextPhase);
        }
    }
}