using System.Numerics;

namespace AAEmu.Game.Models.Game.AI.AStar;

public class AiNavigation
{
    public AiNavigation()
    {
        //
    }

    public AiNavigation(uint id, uint zoneKey, uint startPoint, uint endPoint, float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
        Id = id;
        ZoneKey = zoneKey;
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    public AiNavigation(float x, float y, float z)
    {
        Position = new Vector3(x, y, z);
        Id = 0;
        ZoneKey = 0;
        StartPoint = 0;
        EndPoint = 0;
    }

    public uint Id { get; set; }
    public uint ZoneKey { get; set; }
    public uint StartPoint { get; set; }
    public uint EndPoint { get; set; }
    public Vector3 Position { get; set; }
}
