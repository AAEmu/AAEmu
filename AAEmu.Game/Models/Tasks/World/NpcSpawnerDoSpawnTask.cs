using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World
{
    public class NpcSpawnerDoSpawnTask : Task
    {
        private readonly NpcSpawner _npcSpawner;
        private readonly bool _all;

        public NpcSpawnerDoSpawnTask(NpcSpawner npcSpawner, bool all = false)
        {
            _npcSpawner = npcSpawner;
            _all = all;
        }

        public override void Execute()
        {
            _npcSpawner?.DoSpawn(_all);
        }
    }
}
