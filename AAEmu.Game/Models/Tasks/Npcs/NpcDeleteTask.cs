using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.Npcs;

public class NpcDeleteTask : Task
{
    private Npc _npc;

    public NpcDeleteTask(Npc npc)
    {
        _npc = npc;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        _npc.Simulation.NpcDeleteTask = null;
        _npc.Spawner.DespawnWithRespawn(_npc);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
