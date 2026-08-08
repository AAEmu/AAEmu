using System.Collections.Concurrent;

using NLog;

namespace AAEmu.Game;

/// <summary>
/// Measures zone-mirror scuff between ZWSpawnNpc Z (type1 includes +0.4) and first
/// ZWUnitMovements Z after dedicate phys/AI. Enable with <c>AAEMU_LOG_NPC_HEIGHT=1</c>.
/// See <c>ZONE_COORDS_AND_HEIGHT.md</c> §11.
/// </summary>
public static class NpcHeightDiagnostics
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("AAEMU_LOG_NPC_HEIGHT") == "1";

    /// <summary>
    /// Follow single NPCs end to end: <c>AAEMU_TRACE_NPC_TPL=4175,1234</c> and/or
    /// <c>AAEMU_TRACE_NPC_BC=15729370</c>. Emits spawn Z, every dedicate move Z, the Z actually
    /// written into SCUnitState, and whether each client was sent the corrective movement — which
    /// is the set of facts that separates "dedicate never settles it" from "client never told".
    /// </summary>
    private static readonly HashSet<uint> TraceTemplates = ParseIdList("AAEMU_TRACE_NPC_TPL");
    private static readonly ConcurrentDictionary<uint, byte> TraceBcIds = ToSet(ParseIdList("AAEMU_TRACE_NPC_BC"));

    /// <summary>Arm the trace on whatever the player targets, so a floater can be picked in game.</summary>
    private static readonly bool TraceOnTarget =
        Environment.GetEnvironmentVariable("AAEMU_TRACE_NPC_TARGET") == "1";

    private static bool TraceEnabled => TraceTemplates.Count > 0 || !TraceBcIds.IsEmpty;

    private static readonly ConcurrentDictionary<string, long> TraceThrottle = new();

    private static HashSet<uint> ParseIdList(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var ids = new HashSet<uint>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (uint.TryParse(part, out var id))
                ids.Add(id);
        }

        return ids;
    }

    private static ConcurrentDictionary<uint, byte> ToSet(HashSet<uint> ids)
    {
        var set = new ConcurrentDictionary<uint, byte>();
        foreach (var id in ids)
            set[id] = 0;

        return set;
    }

    /// <summary>
    /// Start following <paramref name="bcId"/>. Called when the player targets a zone mirror under
    /// <c>AAEMU_TRACE_NPC_TARGET=1</c> — target the floater in game and the trace picks it up.
    /// </summary>
    public static void ArmTrace(uint bcId, uint templateId)
    {
        if (!TraceOnTarget || bcId == 0 || !TraceBcIds.TryAdd(bcId, 0))
            return;

        var spawnZ = Spawns.TryGetValue(bcId, out var s) ? s.SpawnWorldZ : float.NaN;
        var minZ = s is { MinMoveZ: < float.MaxValue } ? s.MinMoveZ : float.NaN;
        Logger.Info(
            "NpcTrace armed bc={0} tpl={1} spawnZ={2:F3} minMoveZ={3:F3} maxDrop={4:F3} moveSamples={5}",
            bcId, templateId, spawnZ, minZ, s?.MaxDrop ?? 0f, s?.MoveSamples ?? 0);
    }

    private static bool IsTraced(uint bcId)
    {
        if (!TraceEnabled || bcId == 0)
            return false;
        if (TraceBcIds.ContainsKey(bcId))
            return true;

        return TraceTemplates.Count > 0
               && Spawns.TryGetValue(bcId, out var s)
               && TraceTemplates.Contains(s.TemplateId);
    }

    /// <summary>Rate limiter so a traced NPC in a 500-unit stream stays readable.</summary>
    private static bool TraceDue(string key, int everyMs)
    {
        var now = Environment.TickCount64;
        var due = TraceThrottle.GetOrAdd(key, 0L);
        if (now < due)
            return false;

        TraceThrottle[key] = now + everyMs;
        return true;
    }

    private sealed class SpawnSample
    {
        public uint TemplateId;
        public uint ZoneId;
        public float LocalX, LocalY, SpawnLocalZ;
        public float WorldX, WorldY, SpawnWorldZ;
        public long SpawnTickMs;
        public int MoveSamples;
        public float FirstMoveZ;
        public float MinMoveZ;
        public float MaxDrop; // spawnWorldZ - minMoveZ
    }

    private static readonly ConcurrentDictionary<uint, SpawnSample> Spawns = new();
    private static long _nextSummaryMs;

    public static void RecordSpawn(
        uint bcId,
        uint templateId,
        uint zoneId,
        float localX,
        float localY,
        float localZ,
        float worldX,
        float worldY,
        float worldZ)
    {
        if (!Enabled || bcId == 0)
            return;

        Spawns[bcId] = new SpawnSample
        {
            TemplateId = templateId,
            ZoneId = zoneId,
            LocalX = localX,
            LocalY = localY,
            SpawnLocalZ = localZ,
            WorldX = worldX,
            WorldY = worldY,
            SpawnWorldZ = worldZ,
            SpawnTickMs = Environment.TickCount64,
            MinMoveZ = float.MaxValue
        };
    }

    public static void ObserveMove(uint bcId, float worldX, float worldY, float worldZ)
    {
        if (!Enabled || bcId == 0)
            return;

        if (!Spawns.TryGetValue(bcId, out var s))
            return;

        s.MoveSamples++;
        if (s.MoveSamples == 1)
        {
            s.FirstMoveZ = worldZ;
            var drop = s.SpawnWorldZ - worldZ;
            var ageMs = Environment.TickCount64 - s.SpawnTickMs;
            Logger.Info(
                "NpcHeight settle#1 bc={0} tpl={1} zone={2} ageMs={3} spawnZ={4:F3} moveZ={5:F3} drop={6:F3} " +
                "spawnXY=({7:F1},{8:F1}) moveXY=({9:F1},{10:F1})",
                bcId, s.TemplateId, s.ZoneId, ageMs,
                s.SpawnWorldZ, worldZ, drop,
                s.WorldX, s.WorldY, worldX, worldY);
        }

        if (worldZ < s.MinMoveZ)
        {
            s.MinMoveZ = worldZ;
            s.MaxDrop = s.SpawnWorldZ - worldZ;
        }

        if (IsTraced(bcId) && TraceDue($"move:{bcId}", 1000))
        {
            Logger.Info(
                "NpcTrace move bc={0} tpl={1} z={2:F3} spawnZ={3:F3} drop={4:F3} minZ={5:F3} samples={6} xy=({7:F1},{8:F1})",
                bcId, s.TemplateId, worldZ, s.SpawnWorldZ, s.SpawnWorldZ - worldZ, s.MinMoveZ,
                s.MoveSamples, worldX, worldY);
        }

        var now = Environment.TickCount64;
        if (now < _nextSummaryMs)
            return;

        _nextSummaryMs = now + 30_000;
        WriteSummary();
    }

    /// <summary>Z the client is actually painted with, at the moment SCUnitState leaves World.</summary>
    public static void RecordPaint(uint bcId, uint templateId, string characterName, float z)
    {
        if (!IsTraced(bcId))
            return;

        var spawnZ = Spawns.TryGetValue(bcId, out var s) ? s.SpawnWorldZ : float.NaN;
        var samples = s?.MoveSamples ?? 0;
        Logger.Info(
            "NpcTrace paint bc={0} tpl={1} → {2} scZ={3:F3} spawnZ={4:F3} settledBy={5} moveSamples={6}",
            bcId, templateId, characterName, z, spawnZ,
            samples > 0 ? "move" : "spawn-only", samples);
    }

    /// <summary>
    /// Whether a corrective movement frame for this NPC was actually put on this client's socket.
    /// <paramref name="included"/> false means the AOI/stream filter dropped it.
    /// </summary>
    public static void RecordRelay(uint bcId, string characterName, float z, bool included)
    {
        if (!IsTraced(bcId) || !TraceDue($"relay:{bcId}:{characterName}", 5000))
            return;

        Logger.Info(
            "NpcTrace relay bc={0} → {1} sentToClient={2} z={3:F3}",
            bcId, characterName, included, z);
    }

    public static bool IsTracing(uint bcId) => IsTraced(bcId);

    public static void OnRemove(uint bcId) => Spawns.TryRemove(bcId, out _);

    private static void WriteSummary()
    {
        var all = Spawns.Values.Where(s => s.MoveSamples > 0).ToArray();
        if (all.Length == 0)
        {
            Logger.Info("NpcHeight summary: no moved samples yet (trackedSpawn={0})", Spawns.Count);
            return;
        }

        static int Bucket(float drop) => drop switch
        {
            < 0.1f => 0,
            < 0.3f => 1,
            < 0.5f => 2,
            < 1.0f => 3,
            _ => 4
        };

        var buckets = new int[5];
        foreach (var s in all)
            buckets[Bucket(s.MaxDrop)]++;

        var near04 = all.Count(s => s.MaxDrop is >= 0.25f and <= 0.55f);
        var avgDrop = all.Average(s => s.MaxDrop);
        var medDrop = all.OrderBy(s => s.MaxDrop).ElementAt(all.Length / 2).MaxDrop;

        Logger.Info(
            "NpcHeight summary n={0} avgDrop={1:F3} medDrop={2:F3} near0.4m={3} " +
            "buckets(<0.1,<0.3,<0.5,<1.0,>=1.0)=({4},{5},{6},{7},{8}) tracked={9}",
            all.Length, avgDrop, medDrop, near04,
            buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], Spawns.Count);

        foreach (var s in all.OrderByDescending(x => x.MaxDrop).Take(5))
        {
            Logger.Info(
                "  topDrop bc tpl={0} zone={1} drop={2:F3} spawnZ={3:F2} minMoveZ={4:F2} samples={5}",
                s.TemplateId, s.ZoneId, s.MaxDrop, s.SpawnWorldZ, s.MinMoveZ, s.MoveSamples);
        }
    }
}
