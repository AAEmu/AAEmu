using System.Linq;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncTimerTask : DoodadFuncTask
{
    private static Logger _log = LogManager.GetCurrentClassLogger();
    private BaseUnit _caster;
    private Doodad _owner;
    private uint _skillId;
    private int _nextPhase;

    public DoodadFuncTimerTask(BaseUnit caster, Doodad owner, uint skillId, int nextPhase) : base(caster, owner, skillId)
    {
        _caster = caster;
        _owner = owner;
        _skillId = skillId;
        _nextPhase = nextPhase;
    }
    public override void Execute()
    {
        if (_caster is Character)
            _log.Debug("[Doodad] DoodadFuncTimerTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);
        else
            _log.Trace("[Doodad] DoodadFuncTimerTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _nextPhase);

        _owner.FuncTask = null;
        _owner.DoChangePhase(_caster, _nextPhase);

        // the phase state does not allow us to interact with the object, so we will automatically
        // get items from ID=6121 & ID=6125, "Treasure Chest" in Palace Celler Dungeon
        var doodadFuncs = DoodadManager.Instance.GetFuncsForGroup((uint)_nextPhase);
        if (doodadFuncs.Count > 0)
        {
            foreach (var f in doodadFuncs.Where(f => f.FuncType is "DoodadFuncLootItem" or "DoodadFuncLootPack"))
            {
                if (!_owner.IsGroupKindStart((uint)_nextPhase))
                {
                    _owner.DoFunc(_caster, 0, f);
                }
            }
        }
    }
}
