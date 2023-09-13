using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.Npcs
{
    public class NpcDeleteTask : Task
    {
        private Npc _npc;

        public NpcDeleteTask(Npc npc)
        {
            _npc = npc;
        }

        public override void Execute()
        {
            _npc.Simulation.NpcDeleteTask = null;
            _npc.Spawner.DespawnWithRespawn(_npc);
        }
    }
}
