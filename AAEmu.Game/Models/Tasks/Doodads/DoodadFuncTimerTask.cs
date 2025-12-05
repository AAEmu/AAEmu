using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncTimerTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase)
    : DoodadFuncTask(caster, owner, skillId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly BaseUnit _caster = caster;
    private readonly Doodad _owner = owner;
    private readonly uint _skillId = skillId;

    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncTimerTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, nextPhase);
        else
            Logger.Trace("[Doodad] DoodadFuncTimerTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, nextPhase);

        _owner.FuncTask = null;
        _owner.DoChangePhase(_caster, nextPhase);

        // the phase state does not allow us to interact with the object, so we will automatically
        // get items from ID=6121 & ID=6125, "Treasure Chest" in Palace Celler Dungeon
        var doodadFuncs = DoodadManager.Instance.GetFuncsForGroup((uint)nextPhase);
        if (doodadFuncs.Count > 0)
        {
            foreach (var f in doodadFuncs.Where(f => f.FuncType is "DoodadFuncLootItem" or "DoodadFuncLootPack"))
            {
                if (!_owner.IsGroupKindStart((uint)nextPhase))
                {
                    _owner.DoFunc(_caster, 0, f);
                }
            }
        }
    }
}
