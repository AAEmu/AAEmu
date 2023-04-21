using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class DespawnTask : Task
    {
        private readonly BaseUnit _caster;

        public DespawnTask(BaseUnit caster)
        {
            _caster = caster;
        }
        public override void Execute()
        {
            if (_caster is not Npc npc)
                return;

            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
            npc.Delete();
        }
    }
}
