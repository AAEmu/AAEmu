#nullable enable

using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.Doodads;

public sealed class DoodadDeleteTask(Doodad owner) : Task
{
    public override void Execute()
    {
        owner?.Delete();
    }
}

