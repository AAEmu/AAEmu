using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.World;

public class WaterBodies
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float OceanLevel { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public List<WaterBodyArea> Areas { get; set; }

    [JsonIgnore] public object _lock;

    public WaterBodies()
    {
        _lock = new object();
        Areas = new List<WaterBodyArea>();
    }

    public bool IsWater(Vector3 point)
    {
        if (point.Z <= OceanLevel)
            return true;

        lock (_lock)
        {
            foreach (var area in Areas)
                if (area.IsWater(point))
                    return true;
        }
        return false;
    }

    public float GetWaterSurface(Vector3 point)
    {
        if (point.Z <= OceanLevel)
            return OceanLevel;

        lock (_lock)
        {
            foreach (var area in Areas)
                if (area.GetSurface(point, out var surfacePoint))
                    return surfacePoint.Z;
        }

        return OceanLevel;
    }

    public static bool Save(string fileName, WaterBodies waterBodies)
    {
        try
        {
            lock (waterBodies._lock)
            {
                var jsonString = JsonConvert.SerializeObject(waterBodies, Formatting.Indented);
                File.WriteAllText(fileName, jsonString);
            }
        }
        catch
        {
            return false;
            // Ignore
        }
        return true;
    }

    public static bool Load(string fileName, out WaterBodies waterBodies)
    {
        waterBodies = null;
        try
        {
            var jsonString = File.ReadAllText(fileName);
            if (!JsonHelper.TryDeserializeObject<WaterBodies>(jsonString, out var newData, out var error))
                return false;
            foreach (var area in newData.Areas)
                area.UpdateBounds();
            waterBodies = newData;
        }
        catch
        {
            return false;
            // Ignore
        }
        return true;
    }
}
