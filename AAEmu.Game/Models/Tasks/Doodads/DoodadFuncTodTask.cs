using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncTodTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase)
    : DoodadFuncTask(caster, owner, skillId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly BaseUnit _caster = caster;
    private readonly Doodad _owner = owner;
    private readonly uint _skillId = skillId;

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

        _owner.DoChangePhase(_caster, nextPhase);
    }
}
