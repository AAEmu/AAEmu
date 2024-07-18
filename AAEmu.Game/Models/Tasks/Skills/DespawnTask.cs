using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class DespawnTask : Task
{
    private readonly BaseUnit _caster;

    public DespawnTask(BaseUnit caster)
    {
        _caster = caster;
    }
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        if (_caster is not Npc npc)
            return System.Threading.Tasks.Task.CompletedTask; ;

        ObjectIdManager.Instance.ReleaseId(npc.ObjId);
        npc.Delete();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
