using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World;

public class DoodadSpawnerDoSpawnTask : Task
{
    private readonly DoodadSpawner _doodadSpawner;

    public DoodadSpawnerDoSpawnTask(DoodadSpawner doodadSpawner)
    {
        _doodadSpawner = doodadSpawner;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        _doodadSpawner.DoSpawn();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
