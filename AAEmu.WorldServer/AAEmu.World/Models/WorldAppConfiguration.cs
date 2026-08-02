namespace AAEmu.World.Models;

public class WorldAppConfiguration
{
    public ZoneNetworkConfig ZoneNetwork { get; set; } = new();
    /// <summary>Legacy stub — unused when Game CS/SC is hosted in-process.</summary>
    public ClientNetworkConfig ClientNetwork { get; set; } = new() { Enabled = false };
    public PublicNetworkConfig PublicNetwork { get; set; } = new();
    public GameBridgeNetworkConfig GameBridge { get; set; } = new() { Enabled = false };
    /// <summary>Directory with Game Config.json / Configurations / Data (AAEmu.Game bin output).</summary>
    public string GameContentRoot { get; set; } = "";
    /// <summary>
    /// Extracted game data root (contains <c>worlds/…/zone/&lt;id&gt;/zone_server/npc_spawners.g</c>).
    /// Used to seed <c>WZSpawnerList</c> autoCreated entries. Typically <c>…\Server\game</c>
    /// matching dedic.bat, or <c>…\client\game</c>.
    /// </summary>
    public string ZoneGameDataRoot { get; set; } = "";
    /// <summary>Activate native NPC spawners around players entering a zone.</summary>
    public NpcSpawnerActivateConfig NpcSpawnerActivate { get; set; } = new();
    /// <summary>Hold back spawners that are outside their game_schedule or day-night window.</summary>
    public NpcScheduleGateConfig NpcScheduleGate { get; set; } = new();
}

public class NpcScheduleGateConfig
{
    /// <summary>
    /// Disabling this restores the pre-gate behaviour: every spawner the dedicate arms is accepted,
    /// including live events whose period ended years ago.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Seconds between window re-evaluations. Day-night windows move with the in-game clock, which
    /// runs far faster than wall time, so this is the resolution at which timed spawners appear and
    /// disappear. Values below 5 are clamped.
    /// </summary>
    public int RefreshSeconds { get; set; } = 30;
}

public class NpcSpawnerActivateConfig
{
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Activate from the zone center before any player enters. This can create every NPC at once
    /// and exceed dedicate's per-frame AI physics budget, so it is disabled by default.
    /// </summary>
    public bool PrewarmOnZoneLoaded { get; set; }
    /// <summary>
    /// Fallback center when zone group / xml origin cannot be resolved (Gweonid bring-up default).
    /// Live activate prefers ZoneGroup AABB or xml zone origin for <c>connection.ZoneId</c>.
    /// </summary>
    public float X { get; set; } = 10322.5f;
    public float Y { get; set; } = 16014.4f;
    public float Z { get; set; } = 356.6f;
    /// <summary>Radius around the entering player, or around the zone center when prewarming.</summary>
    public float Radius { get; set; } = 1024f;
}

public class ZoneNetworkConfig
{
    public string Host { get; set; } = "*";
    public int Port { get; set; } = 1240;
}

public class ClientNetworkConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "*";
    public int Port { get; set; } = 1239;
}

public class GameBridgeNetworkConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "*";
    public int Port { get; set; } = 1241;
}

public class PublicNetworkConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1239;
}
