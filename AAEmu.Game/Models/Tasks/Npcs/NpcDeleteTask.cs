using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.Npcs;

public class NpcDeleteTask(Npc npc) : Task
{
    public override void Execute()
    {
        npc.Simulation.NpcDeleteTask = null;
        npc.Spawner.DespawnWithRespawn(npc);
    }
}
