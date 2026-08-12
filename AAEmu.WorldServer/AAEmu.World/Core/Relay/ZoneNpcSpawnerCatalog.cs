using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.World.Core.Network;
using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Persistent spawner seed for <c>WZSpawnerList</c> (0x005) — saved indun spawner state.
/// </summary>
/// <remarks>
/// Zone applies the received list in <c>NpcManager::ApplyPersistentSpawners</c>
/// matched by <c>id</c> against the manager's spawnerId→NpcSpawner map and is applied only
/// when <c>NpcSpawner+0x158</c> is set; every other entry lands in the routine's
/// <c>incompatible</c> counter and is discarded.
///
/// <c>NpcSpawner+0x158</c> is written in exactly one place —
/// the world is an indun (<c>world+0x8C == 2</c>), the desc carries <c>save_indun</c>
/// and no member is an npc group. Seamless open-world zones therefore never accept a single
/// entry, and the empty <c>last=1</c> chunk is the correct payload for them.
///
/// This channel is unrelated to arming autoCreated/quest NPCs; those come up through
/// <c>WZActivateNpcSpawnersInArea</c> (0x042).
/// </remarks>
public static partial class ZoneNpcSpawnerCatalog
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<uint, IReadOnlyList<PersistentSpawnerEntry>> CatalogCache = new();
    private static readonly ConcurrentDictionary<PersistentSpawnerKey, PersistentSpawnerState> InstanceStates = new();

    private static readonly Regex SpawnerIdRegex = SpawnerIdPattern();
    private static readonly Regex SpawnerTypeRegex = SpawnerTypePattern();

    public readonly record struct PersistentSpawnerEntry(uint Id, uint Type, byte State);
    private readonly record struct PersistentSpawnerKey(uint InstanceId, uint ZoneId, uint SpawnerId);
    private readonly record struct PersistentSpawnerState(uint Type, byte State);

    public static IReadOnlyList<PersistentSpawnerEntry> GetPersistentSpawners(uint zoneId, uint instanceId)
    {
        if (zoneId == 0)
            return [];
        if (string.Equals(Environment.GetEnvironmentVariable("AAEMU_WZ_EMPTY_SPAWNER_LIST"), "1",
                StringComparison.Ordinal))
            return [];

        var catalog = CatalogCache.GetOrAdd(zoneId, LoadPersistentSpawners);
        if (catalog.Count == 0)
            return catalog;

        var result = new PersistentSpawnerEntry[catalog.Count];
        for (var i = 0; i < catalog.Count; i++)
        {
            var entry = catalog[i];
            var key = new PersistentSpawnerKey(instanceId, zoneId, entry.Id);
            var state = InstanceStates.AddOrUpdate(
                key,
                new PersistentSpawnerState(entry.Type, entry.State),
                (_, current) => current.Type == entry.Type
                    ? current
                    : new PersistentSpawnerState(entry.Type, entry.State));
            result[i] = entry with { State = state.State };
        }

        return result;
    }

    public static void Invalidate(uint zoneId = 0)
    {
        if (zoneId == 0)
        {
            CatalogCache.Clear();
            InstanceStates.Clear();
        }
        else
        {
            CatalogCache.TryRemove(zoneId, out _);
            foreach (var key in InstanceStates.Keys)
                if (key.ZoneId == zoneId)
                    InstanceStates.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Record the live state of a native persistent-instance spawner. Unknown placements and
    /// non-persistent templates are ignored; the catalog is the authority for both id and type.
    /// </summary>
    public static bool SetState(ZoneConnection connection, ZwSpawnNpcParsed spawn, bool active)
    {
        if (connection == null || spawn == null)
            return false;

        var catalog = CatalogCache.GetOrAdd(connection.ZoneId, LoadPersistentSpawners);
        PersistentSpawnerEntry? matched = null;
        foreach (var candidate in catalog)
        {
            if (candidate.Id != spawn.SpawnerId || candidate.Type != spawn.SpawnerType)
                continue;
            matched = candidate;
            break;
        }

        if (matched is not { } entry)
            return false;

        var key = new PersistentSpawnerKey(connection.InstanceId, connection.ZoneId, entry.Id);
        var next = new PersistentSpawnerState(entry.Type, active ? (byte)1 : (byte)0);
        var changed = false;
        InstanceStates.AddOrUpdate(
            key,
            _ =>
            {
                changed = entry.State != next.State;
                return next;
            },
            (_, current) =>
            {
                changed = current != next;
                return next;
            });

        if (changed)
        {
            Logger.Info(
                "Persistent NPC spawner instanceId={0} zoneId={1} id={2} type={3} state={4}",
                connection.InstanceId, connection.ZoneId, entry.Id, entry.Type, next.State);
        }

        return true;
    }

    /// <summary>Resolve a tracked Zone unit back to its exact SpawnNpc placement and mark it dead.</summary>
    public static bool MarkUnitDead(ZoneConnection connection, uint bcId)
    {
        if (connection == null || !connection.Units.TryGet(bcId, out var raw) || raw == null)
            return false;

        var spawn = ZwSpawnNpcParser.TryParse(raw);
        return spawn != null && SetState(connection, spawn, active: false);
    }

    /// <summary>
    /// Forget all saved spawner state when the owning World instance is destroyed. Instance ids
    /// are recycled, so retaining state beyond this boundary would leak one dungeon run into another.
    /// </summary>
    public static void RemoveInstance(uint instanceId)
    {
        var removed = 0;
        foreach (var key in InstanceStates.Keys)
        {
            if (key.InstanceId == instanceId && InstanceStates.TryRemove(key, out _))
                removed++;
        }

        if (removed > 0)
            Logger.Info("Cleared {0} persistent NPC spawner states for World instanceId={1}", removed, instanceId);
    }

    private static IReadOnlyList<PersistentSpawnerEntry> LoadPersistentSpawners(uint zoneId)
    {
        var world = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (world is not { XmlWorld.IsInstance: > 0 })
        {
            Logger.Info(
                "WZSpawnerList zoneId={0} world={1} is not an indun — sending the empty last=1 gate",
                zoneId, world?.Name ?? "<unresolved>");
            return [];
        }

        var path = ResolveSpawnerFile(zoneId, world.Name);
        if (path is null)
        {
            Logger.Warn(
                "WZSpawnerList: no npc_spawners.g for indun zoneId={0} under ZoneGameDataRoot — saved spawner state will not restore",
                zoneId);
            return [];
        }

        var placements = ParseSpawnerPlacements(path);
        if (placements.Count == 0)
        {
            Logger.Warn("WZSpawnerList: parsed 0 spawners from {0}", path);
            return [];
        }

        var result = new List<PersistentSpawnerEntry>(placements.Count);
        var skippedNoTpl = 0;
        var skippedNotSaved = 0;
        var skippedPopulation = 0;
        var skippedGroup = 0;
        foreach (var (id, type) in placements)
        {
            var template = NpcGameData.Instance.GetNpcSpawnerTemplate(type);
            if (template is null)
            {
                skippedNoTpl++;
                continue;
            }

            if (!template.SaveIndun)
            {
                skippedNotSaved++;
                continue;
            }

            if (template.MaxPopulation != 1)
            {
                skippedPopulation++;
                continue;
            }

            if (HasNpcGroupMember(template))
            {
                skippedGroup++;
                continue;
            }

            // persistent-spawn event 3; state==0 dispatches clear event 4. The desc activation
            // state is only the initial value. GetPersistentSpawners overlays the live instance.
            var state = template.ActivationState ? (byte)1 : (byte)0;
            result.Add(new PersistentSpawnerEntry(id, type, state));
        }

        Logger.Info(
            "WZSpawnerList catalog zoneId={0} world={1} file={2} placements={3} persistent={4} " +
            "(skip notSaved={5} population={6} group={7} noTpl={8})",
            zoneId, world.Name, path, placements.Count, result.Count,
            skippedNotSaved, skippedPopulation, skippedGroup, skippedNoTpl);
        return result;
    }

    private static bool HasNpcGroupMember(AAEmu.Game.Models.Game.NPChar.NpcSpawnerTemplate template)
    {
        if (template.Npcs is null)
            return false;

        foreach (var npc in template.Npcs)
        {
            if (string.Equals(npc.MemberType, "NpcGroup", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ResolveSpawnerFile(uint zoneId, string worldName)
    {
        foreach (var root in EnumerateGameRoots())
        {
            var path = Path.Combine(
                root,
                "worlds",
                worldName,
                "level_design",
                "zone",
                zoneId.ToString(),
                "zone_server",
                "npc_spawners.g");
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateGameRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = ZoneGameDataRootResolver.TryGetRoot();
        if (root != null)
            seen.Add(root);
        return seen;
    }

    private static List<(uint Id, uint Type)> ParseSpawnerPlacements(string path)
    {
        var text = File.ReadAllText(path);
        var blocks = SplitSpawnerBlocks().Split(text);
        var list = new List<(uint, uint)>(blocks.Length);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;
            var idMatch = SpawnerIdRegex.Match(block);
            var typeMatch = SpawnerTypeRegex.Match(block);
            if (!idMatch.Success || !typeMatch.Success)
                continue;
            if (!uint.TryParse(idMatch.Groups[1].Value, out var id))
                continue;
            if (!uint.TryParse(typeMatch.Groups[1].Value, out var type))
                continue;
            list.Add((id, type));
        }

        return list;
    }

    [GeneratedRegex(@"spawnerId\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerIdPattern();

    [GeneratedRegex(@"spawnerType\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SpawnerTypePattern();

    [GeneratedRegex(@"(?m)^spawner\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex SplitSpawnerBlocks();
}
