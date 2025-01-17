using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.World;

public class NpcSpawnerDoDespawnTask : Task
{
    private readonly Npc _npc;

    public NpcSpawnerDoDespawnTask(Npc npc)
    {
        _npc = npc;
    }
    public override void Execute()
    {
        _npc?.Spawner?.DoDespawn([_npc]);
    }
}
