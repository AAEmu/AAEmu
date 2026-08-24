using AAEmu.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using NLog;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Models.Game.Indun;

public class DungeonLoaderTask(WorldTemplate worldTemplate, Dungeon dungeon, uint dungeonInstanceId, Character notifyPlayer) : Task
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        if (dungeon.World == null)
        {
            Logger.Debug($"[???-{worldTemplate.Name}({worldTemplate.Id})] Creating new dungeon instance of  ...");
            dungeon.World = WorldManager.Instance.CreateWorldInstance(worldTemplate, 0, true, dungeonInstanceId, notifyPlayer);
            dungeon.World.DungeonInstance = dungeon;
            Logger.Info($"[{dungeon.World})] New Dungeon instance created!");
        }

        // System instances (Mall AutoCreate) share a pre-started host. Do not spawn extra ZoneHosts.
        var spawnedHost = false;
        if (!dungeon.IsSystem)
        {
            spawnedHost = WorldIntegration.TryStartInstanceZoneHost?.Invoke(dungeon.World) == true;
            if (ShouldAbortMissingSpawnedHost(dungeon.IsSystem, WorldIntegration.ZoneHostSpawnEnabled, spawnedHost))
            {
                FailNoHost(dungeon, "ZoneHost process did not stay running");
                return;
            }
        }

        Logger.Debug($"[{dungeon.World})] Spawning game objects Npc, Doodad, Slave, Gimmick...");
        EnsureDungeonContentSpawned(dungeon.World);
        Logger.Debug($"[{dungeon.World})] Finished spawning game objects Npc, Doodad, Slave, Gimmick...");

        dungeon.RegisterIndunEvents();

        if (!dungeon.IsSystem && !WaitForZoneHost(dungeon.World, requireExactCopy: spawnedHost))
        {
            FailNoHost(dungeon, $"No ZoneLoaded host for zone copy {dungeon.World.Id}");
            return;
        }

        Logger.Info($"[{dungeon.World})] Dungeon instance ready!");
        dungeon.FinishedLoading = true;

        if (dungeon.EnterRequests.Count > 0)
        {
            Logger.Info($"[{dungeon.World})] Moving players to dungeon instance ...");
            foreach (var dungeonEnterRequestPlayer in dungeon.EnterRequests)
            {
                if (dungeonEnterRequestPlayer?.IsOnline ?? false)
                    dungeon.AddPlayer(dungeonEnterRequestPlayer);
            }

            dungeon.EnterRequests.Clear();
        }
    }

    /// <summary>
    /// Spawn permanent dungeon content once. Warm idle worlds and the loader share this so claim
    /// does not double-spawn.
    /// </summary>
    public static void EnsureDungeonContentSpawned(WorldInstance world)
    {
        if (world?.SpawnManager == null)
            return;
        if (world.DungeonContentSpawned)
            return;

        world.SpawnManager.SpawnAll();
        var spawnTasks = world.SpawnManager.SpawnTasks;
        if (spawnTasks is { Count: > 0 })
            System.Threading.Tasks.Task.WhenAll(spawnTasks).GetAwaiter().GetResult();
        world.DungeonContentSpawned = true;
    }

    /// <summary>
    /// World-started copies must not sit on a 120s wait when the process already exited.
    /// Manual Zone Manager hosts still wait (<paramref name="spawnEnabled"/> false).
    /// </summary>
    internal static bool ShouldAbortMissingSpawnedHost(bool isSystem, bool spawnEnabled, bool started) =>
        !isSystem && spawnEnabled && !started;

    private static void FailNoHost(Dungeon dungeon, string reason)
    {
        Logger.Error("[{0}] {1} — players will not be moved in", dungeon.World, reason);
        foreach (var waiting in dungeon.EnterRequests)
        {
            if (waiting?.IsOnline == true)
                waiting.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
        }

        dungeon.EnterRequests.Clear();
        dungeon.DestroyDungeon();
    }

    /// <summary>
    /// Used by warm-pool pre-spawn to wait for ZoneLoaded before <see cref="EnsureDungeonContentSpawned"/>.
    /// </summary>
    public static bool WaitForZoneHostReady(WorldInstance world, bool requireExactCopy = true) =>
        WaitForZoneHost(world, requireExactCopy);

    private static bool WaitForZoneHost(WorldInstance world, bool requireExactCopy)
    {
        var zoneId = world.Template?.ZoneKeys?.Count > 0 ? world.Template.ZoneKeys[0] : 0;
        if (zoneId == 0)
            return false;

        var timeoutSeconds = WorldIntegration.ZoneHostReadyTimeoutSeconds > 0
            ? WorldIntegration.ZoneHostReadyTimeoutSeconds
            : 120;
        var raw = Environment.GetEnvironmentVariable("AAEMU_ZONE_HOST_READY_SECONDS");
        if (int.TryParse(raw, out var configured) && configured > 0)
            timeoutSeconds = configured;

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (IsHostReady(zoneId, world.Id, requireExactCopy))
                return true;
            Thread.Sleep(250);
        }

        return IsHostReady(zoneId, world.Id, requireExactCopy);
    }

    /// <summary>
    /// World-started copies must match (zoneId, world.Id). A unique leftover host of the same
    /// zone key is only accepted when ZoneHost did not start a process (manual Launch).
    /// </summary>
    internal static bool IsHostReady(uint zoneId, uint worldId, bool requireExactCopy)
    {
        if (WorldIntegration.IsZoneInstanceLoaded?.Invoke(zoneId, worldId) == true)
            return true;
        if (!requireExactCopy && WorldIntegration.IsZoneLoaded?.Invoke(zoneId) == true)
            return true;
        return false;
    }
}
