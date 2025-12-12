using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills;

public class DespawnTask(BaseUnit caster) : Task
{
    public override void Execute()
    {
        if (caster is not Npc npc)
            return;

        ObjectIdManager.Instance.ReleaseId(npc.ObjId);
        npc.Delete();
    }
}
