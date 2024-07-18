using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World;

public class NpcSpawnerDoSpawnTask : Task
{
    private readonly NpcSpawner _npcSpawner;
    private readonly bool _all;

    public NpcSpawnerDoSpawnTask(NpcSpawner npcSpawner, bool all = false)
    {
        _npcSpawner = npcSpawner;
        _all = all;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        _npcSpawner?.DoSpawn(_all);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
