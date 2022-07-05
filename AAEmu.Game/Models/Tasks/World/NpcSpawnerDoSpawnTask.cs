using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World
{
    public class NpcSpawnerDoSpawnTask : Task
    {
        private readonly NpcSpawner _npcSpawner;

        public NpcSpawnerDoSpawnTask(NpcSpawner npcSpawner)
        {
            _npcSpawner = npcSpawner;
        }

        public override void Execute()
        {
            _npcSpawner.DoSpawn();
        }
    }
}
