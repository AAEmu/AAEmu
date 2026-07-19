using System.Numerics;

namespace AAEmu.Game.Models.Game.World.Zones;

public class Area
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public List<Vector3> Points { get; set; } = [];
}
