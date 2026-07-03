namespace AAEmu.Game.Models.Json;

/// <summary>
/// A converted npc_spawners.g placement (npc_spawners.g.json), in world coordinates
/// and keyed by the compact.sqlite3 npc_spawners template id. Produced by
/// AAEmu.SpawnConverter.
/// </summary>
public class JsonNpcSpawnerG
{
    /// <summary>Unique placement id from the .g file.</summary>
    public uint SpawnerId { get; set; }

    /// <summary>npc_spawners.id — the compact spawner template that drives this placement.</summary>
    public uint SpawnerType { get; set; }

    /// <summary>"point" or "area".</summary>
    public string AreaType { get; set; }

    /// <summary>Representative world position (single point or polygon centroid).</summary>
    public JsonGSpawnPos Position { get; set; }

    /// <summary>Discrete candidate points for a multi-point spawner; null when there is only one.</summary>
    public List<JsonGSpawnPos> Points { get; set; }

    /// <summary>Weighted triangulation of the roaming polygon for area spawners.</summary>
    public List<JsonGAreaTriangle> Area { get; set; }

    /// <summary>AIPath name to follow after spawning.</summary>
    public string FollowPath { get; set; }

    /// <summary>Start point index along <see cref="FollowPath"/>.</summary>
    public int PathPointNo { get; set; }
}

public class JsonGSpawnPos
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Facing around the Z axis, in radians.</summary>
    public float Yaw { get; set; }
}

public class JsonGAreaTriangle
{
    public JsonGSpawnPos A { get; set; }
    public JsonGSpawnPos B { get; set; }
    public JsonGSpawnPos C { get; set; }
    public float Rate { get; set; }
}
