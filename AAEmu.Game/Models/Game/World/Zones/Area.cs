using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.World.Zones;

public class Area
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public List<Point> _points { get; set; } = new List<Point>();
}
