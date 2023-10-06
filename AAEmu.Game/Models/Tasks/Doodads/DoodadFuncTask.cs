using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads;

public abstract class DoodadFuncTask : Task
{
    //private BaseUnit _caster;
    //private Doodad _owner;
    //private uint _skillId;

    protected DoodadFuncTask(BaseUnit caster, Doodad owner, uint skillId)
    {
        //_caster = caster;
        //_owner = owner;
        //_skillId = skillId;
        //Logger.Warn("[Doodad] DoodadFuncTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
    }
}
