using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World;

public class NpcSpawnerDoDespawnTask(Npc npc) : Task
{
    public override void Execute()
    {
        npc?.Spawner?.DoDespawn(npc);
    }
}
