using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSpawner : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Count { get; set; }

        public override Npc Spawn(uint objId)
        {
            var npc = NpcManager.Instance.Create(objId, UnitId);
            npc.Spawner = this;
            npc.Position = Position.Clone();
            if (npc.Position == null)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}", Id, UnitId);
                return null;
            }

            npc.Spawn();
            return npc;
        }

        public override void Despawn(Npc npc)
        {
            npc.Delete();
        }
    }
}