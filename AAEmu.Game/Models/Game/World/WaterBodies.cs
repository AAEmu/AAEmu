using System;
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

    public bool IsWater(Vector3 point, out Vector3 flowDirection)
    {
        flowDirection = Vector3.Zero;
        
        if (point.Z <= OceanLevel)
            return true;

        lock (_lock)
        {
            // TODO: take the top-most water area in case of overlaps
            foreach (var area in Areas)
            {
                if (area.GetSurface(point, out var surfacePoint, out flowDirection) &&
                    (point.Z <= surfacePoint.Z) &&
                    (point.Z >= surfacePoint.Z - area.Depth))
                    return true;
            }

        }
        flowDirection = Vector3.Zero;
        return false;
    }

    public float GetWaterSurface(Vector3 point, out Vector3 flowDirection)
    {
        flowDirection = Vector3.Zero;
        
        if (point.Z <= OceanLevel)
            return OceanLevel;

        lock (_lock)
        {
            foreach (var area in Areas)
                if (area.GetSurface(point, out var surfacePoint, out flowDirection))
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
            {
                // In effort to removing Height in favor of Depth, recalculate Z
                if (area.Height > 0f)
                {
                    area.Depth = area.Height;
                    area.Height = 0f;
                    for (var i = 0; i <= area.Points.Count - 1; i++)
                    {
                        var p = area.Points[i];
                        area.Points[i] = new Vector3(p.X, p.Y, p.Z + area.Depth);
                    }
                }
                area.UpdateBounds();
            }

            waterBodies = newData;
        }
        catch
        {
            return false;
            // Ignore
        }
        return true;
    }

    public uint GetNewId()
    {
        var res = 1000000u;
        
        foreach (var area in Areas)
        {
            if (area.Id >= res)
                res = area.Id + 1;
        }

        return res;
    }
}
