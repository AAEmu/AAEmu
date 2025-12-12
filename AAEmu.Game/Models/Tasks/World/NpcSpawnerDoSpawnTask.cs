using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World;

public class NpcSpawnerDoSpawnTask(NpcSpawner npcSpawner, bool all = false) : Task
{
    private readonly bool _all = all;

    public override void Execute()
    {
        npcSpawner?.DoSpawn();
    }
}
