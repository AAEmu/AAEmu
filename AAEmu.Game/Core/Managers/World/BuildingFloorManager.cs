using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Manages custom building floor zones that override terrain height.
/// Zones are defined per-world in JSON files and can be added/removed in-game.
/// Each zone is a 4-point polygon with a fixed floor height.
/// </summary>
public class BuildingFloorManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly List<BuildingFloorZone> _zones = [];
    private readonly Lock _lock = new();
    private string _filePath;

    /// <summary>
    /// Loads building floor zones from the world data directory.
    /// </summary>
    public void Load(string worldName)
    {
        _filePath = Path.Combine(FileManager.AppPath, "Data", "Worlds", worldName, "building_floors.json");

        lock (_lock)
        {
            _zones.Clear();

            if (!File.Exists(_filePath))
            {
                Logger.Info($"No building_floors.json found for {worldName}, starting empty.");
                return;
            }

            var contents = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(contents))
                return;

            if (JsonHelper.TryDeserializeObject(contents, out List<BuildingFloorZone> zones, out var error))
            {
                _zones.AddRange(zones);
                Logger.Info($"Loaded {_zones.Count} building floor zones for {worldName}.");
            }
            else
            {
                Logger.Error($"Failed to parse {_filePath}: {error?.Message}");
            }
        }
    }

    /// <summary>
    /// Gets the building floor height at a given position.
    /// Returns 0 if the position is not inside any defined zone.
    /// </summary>
    public float GetFloorHeight(float x, float y, float z)
    {
        lock (_lock)
        {
            var bestZ = 0f;
            var bestDist = float.MaxValue;

            foreach (var zone in _zones)
            {
                if (!zone.ContainsXY(x, y))
                    continue;

                var dist = MathF.Abs(z - zone.FloorZ);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestZ = zone.FloorZ;
                }
            }

            return bestZ;
        }
    }

    /// <summary>
    /// Adds a new building floor zone from 4 corner points and saves to disk.
    /// FloorZ is taken from the first point's Z coordinate.
    /// </summary>
    public BuildingFloorZone AddZone(string name, Vector2[] points, float floorZ)
    {
        var zone = new BuildingFloorZone
        {
            Name = name,
            Points =
            [
                new FloorPoint { X = points[0].X, Y = points[0].Y },
                new FloorPoint { X = points[1].X, Y = points[1].Y },
                new FloorPoint { X = points[2].X, Y = points[2].Y },
                new FloorPoint { X = points[3].X, Y = points[3].Y }
            ],
            FloorZ = floorZ
        };

        lock (_lock)
        {
            _zones.Add(zone);
        }

        Save();
        return zone;
    }

    /// <summary>
    /// Removes the closest zone to the given position (within 50 units).
    /// Returns the removed zone or null if none found.
    /// </summary>
    public BuildingFloorZone RemoveClosest(float x, float y)
    {
        BuildingFloorZone closest = null;

        lock (_lock)
        {
            var closestDist = 50f * 50f;

            foreach (var zone in _zones)
            {
                var center = zone.GetCenter();
                var dx = x - center.X;
                var dy = y - center.Y;
                var dist = dx * dx + dy * dy;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = zone;
                }
            }

            if (closest != null)
                _zones.Remove(closest);
        }

        if (closest != null)
            Save();

        return closest;
    }

    /// <summary>
    /// Returns all zones within the given radius of a position (from zone center).
    /// </summary>
    public List<BuildingFloorZone> GetNearbyZones(float x, float y, float radius)
    {
        var result = new List<BuildingFloorZone>();
        var radiusSq = radius * radius;

        lock (_lock)
        {
            foreach (var zone in _zones)
            {
                var center = zone.GetCenter();
                var dx = x - center.X;
                var dy = y - center.Y;
                var dist = dx * dx + dy * dy;
                if (dist <= radiusSq)
                    result.Add(zone);
            }
        }

        return result;
    }

    public int Count
    {
        get { lock (_lock) return _zones.Count; }
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(_filePath))
            return;

        List<BuildingFloorZone> snapshot;
        lock (_lock)
        {
            snapshot = [.. _zones];
        }

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(_filePath, json);
            Logger.Debug($"Saved {snapshot.Count} building floor zones to {_filePath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save building floor zones: {ex.Message}");
        }
    }
}

/// <summary>
/// A 2D point used in floor zone polygons.
/// </summary>
public class FloorPoint
{
    [JsonProperty("x")]
    public float X { get; set; }

    [JsonProperty("y")]
    public float Y { get; set; }
}

/// <summary>
/// Defines a 4-point polygon area with a fixed floor height.
/// Used to override terrain height inside buildings.
/// </summary>
public class BuildingFloorZone
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("points")]
    public List<FloorPoint> Points { get; set; } = [];

    [JsonProperty("floorZ")]
    public float FloorZ { get; set; }

    /// <summary>
    /// Checks if a point (x, y) is inside this zone's polygon using ray casting.
    /// </summary>
    public bool ContainsXY(float x, float y)
    {
        if (Points.Count < 3)
            return false;

        // Quick bounding box check first
        var minX = float.MaxValue;
        var maxX = float.MinValue;
        var minY = float.MaxValue;
        var maxY = float.MinValue;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        if (x < minX || x > maxX || y < minY || y > maxY)
            return false;

        // Ray casting algorithm
        var inside = false;
        for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
        {
            var pi = Points[i];
            var pj = Points[j];

            if ((pi.Y > y) != (pj.Y > y) &&
                x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Returns the centroid of the polygon.
    /// </summary>
    public Vector2 GetCenter()
    {
        if (Points.Count == 0)
            return Vector2.Zero;

        var cx = 0f;
        var cy = 0f;
        foreach (var p in Points)
        {
            cx += p.X;
            cy += p.Y;
        }

        return new Vector2(cx / Points.Count, cy / Points.Count);
    }
}
