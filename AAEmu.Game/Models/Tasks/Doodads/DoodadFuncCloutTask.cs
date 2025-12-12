using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncCloutTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase, AreaTrigger areaTrigger)
    : DoodadFuncTask(caster, owner, skillId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly BaseUnit _caster = caster;
    private readonly Doodad _owner = owner;
    private readonly uint _skillId = skillId;

    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, nextPhase);
        else
            Logger.Trace("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, nextPhase);

        _owner.FuncTask = null;

        if (nextPhase == -1)
            _owner.Delete();

        AreaTriggerManager.Instance.RemoveAreaTrigger(areaTrigger);
        _owner.DoChangePhase(_caster, nextPhase);
    }
}
