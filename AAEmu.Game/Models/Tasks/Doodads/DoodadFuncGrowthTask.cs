using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncGrowthTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase, float endScale)
    : DoodadFuncTask(caster, owner, skillId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly BaseUnit _caster = caster;
    private readonly Doodad _owner = owner;
    private readonly uint _skillId = skillId;

    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncGrowthTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
        else
            Logger.Trace("[Doodad] DoodadFuncGrowthTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);

        _owner.SetScale(endScale);

        _owner.FuncTask = null;

        _owner.DoChangePhase(_caster, nextPhase);
    }
}
