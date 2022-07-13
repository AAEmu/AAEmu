using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World
{
    public class DoodadSpawnerDoSpawnTask : Task
    {
        private readonly DoodadSpawner _doodadSpawner;

        public DoodadSpawnerDoSpawnTask(DoodadSpawner doodadSpawner)
        {
            _doodadSpawner = doodadSpawner;
        }

        public override void Execute()
        {
            _doodadSpawner.DoSpawn();
        }
    }
}
