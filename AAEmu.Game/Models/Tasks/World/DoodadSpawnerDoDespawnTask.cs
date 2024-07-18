using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World;

public class DoodadSpawnerDoDespawnTask : Task
{
    private readonly Doodad _doodad;

    public DoodadSpawnerDoDespawnTask(Doodad doodad)
    {
        _doodad = doodad;
    }
    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        _doodad.DoDespawn(_doodad);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
