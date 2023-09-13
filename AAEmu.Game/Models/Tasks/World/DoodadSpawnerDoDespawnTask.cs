using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World
{
    public class DoodadSpawnerDoDespawnTask : Task
    {
        private readonly Doodad _doodad;

        public DoodadSpawnerDoDespawnTask(Doodad doodad)
        {
            _doodad = doodad;
        }
        public override void Execute()
        {
            _doodad.DoDespawn(_doodad);
        }
    }
}
