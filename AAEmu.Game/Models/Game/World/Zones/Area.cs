using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.Game.World.Zones;

public class Area
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public List<JVector> Points { get; set; } = [];
}
