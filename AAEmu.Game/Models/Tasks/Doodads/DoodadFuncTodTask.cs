using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncTodTask : DoodadFuncTask
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private BaseUnit _caster;
    private Doodad _owner;
    private uint _skillId;
    private int _nextPhase;

    public DoodadFuncTodTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase) : base(caster, owner, skillId)
    {
        _caster = caster;
        _owner = owner;
        _skillId = skillId;
        _nextPhase = nextPhase;
    }
    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncTodTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
        else
            Logger.Trace("[Doodad] DoodadFuncTodTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);

        if (_owner.FuncTask != null)
        {
            _owner.FuncTask.Cancel();
            _owner.FuncTask = null;
            if (_caster is Character)
                Logger.Debug("DoodadFuncTodTask: The current timer has been ended.");
            else
                Logger.Trace("DoodadFuncTodTask: The current timer has been ended.");
        }

        _owner.DoChangePhase(_caster, _nextPhase);
    }
}
