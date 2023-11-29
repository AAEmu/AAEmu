using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncCloutTask : DoodadFuncTask
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private BaseUnit _caster;
    private Doodad _owner;
    private uint _skillId;
    private int _nextPhase;
    private AreaTrigger _areaTrigger;

    public DoodadFuncCloutTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase, AreaTrigger areaTrigger) : base(caster, owner, skillId)
    {
        _caster = caster;
        _owner = owner;
        _skillId = skillId;
        _nextPhase = nextPhase;
        _areaTrigger = areaTrigger;
    }

    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);
        else
            Logger.Trace("[Doodad] DoodadFuncCloutTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);

        _owner.FuncTask = null;

        if (_nextPhase == -1)
            _owner.Delete();

        AreaTriggerManager.Instance.RemoveAreaTrigger(_areaTrigger);
        _owner.DoChangePhase(_caster, _nextPhase);
    }
}
