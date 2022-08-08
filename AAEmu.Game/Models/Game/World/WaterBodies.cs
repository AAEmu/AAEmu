using System.Collections.Generic;
using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

public class WaterBodies
{
    public float OceanLevel { get; set; }
    public List<WaterBodyArea> Areas { get; set; }

    public WaterBodies()
    {
        OceanLevel = 100f;
        Areas = new List<WaterBodyArea>();
    }

    public bool IsWater(Vector3 point)
    {
        if (point.Z <= OceanLevel)
            return true;

        foreach (var area in Areas)
            if (area.IsWater(point))
                return true;

        return false;
    }

    public float GetWaterSurface(Vector3 point)
    {
        if (point.Z <= OceanLevel)
            return OceanLevel;
        
        foreach (var area in Areas)
            if (area.GetSurface(point, out var surfacePoint))
                return surfacePoint.Z;
        
        return OceanLevel;
    }
}
