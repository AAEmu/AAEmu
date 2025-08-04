using System.Numerics;

namespace AAEmu.Game.Models.Game.AI.AStar;

public class AreasMissionPoints
{
    public AreasMissionPoints()
    {
    }

    public AreasMissionPoints(uint id, uint zoneKey, uint startPoint, uint endPoint, float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
        Id = id;
        ZoneKey = zoneKey;
    }

    public AreasMissionPoints(float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
        Id = 0;
        ZoneKey = 0;
    }

    public uint Id { get; set; }
    public uint ZoneKey { get; set; }
    public Vector3 Position { get; set; }
}
