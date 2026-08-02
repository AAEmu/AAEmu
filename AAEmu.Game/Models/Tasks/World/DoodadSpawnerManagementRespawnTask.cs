using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World;

public class DoodadSpawnerManagementRespawnTask(DoodadSpawner doodadSpawner) : Task
{
    public override void Execute()
    {
        doodadSpawner.DoManagementRespawn();
    }
}
