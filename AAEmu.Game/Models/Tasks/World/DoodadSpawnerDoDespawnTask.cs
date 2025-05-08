using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World;

public class DoodadSpawnerDoDespawnTask(Doodad doodad) : Task
{
    public override void Execute()
    {
        doodad.DoDespawn();
    }
}
