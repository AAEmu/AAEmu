using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.World;

public class WorldSpawnLocation
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public WorldSpawnPosition SpawnPosition { get; set; }

    public override string ToString()
    {
        return Name + " - " + SpawnPosition;
    }
}
