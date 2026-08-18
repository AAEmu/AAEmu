using System.Diagnostics;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Tasks.Network;
using AAEmu.Game.Utils.Scripts;

using Microsoft.Extensions.Hosting;

using NLog;

namespace AAEmu.Game;

public sealed class GameService : IHostedService, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static TimeProvider s_timeProvider = TimeProvider.System;
    private Timer _gameKeepaliveTimer;
    public static DateTime StartTime { get; private set; } = DateTime.UtcNow;
    public static TimeSpan TimeSinceStart => s_timeProvider.GetUtcNow().UtcDateTime.Subtract(StartTime);

    private readonly ManagerOrchestrator _orchestrator;

    public GameService(IServiceProvider serviceProvider, ManagerOrchestrator orchestrator, TimeProvider timeProvider)
    {
        SingletonContainer.ServiceProvider = serviceProvider;
        _orchestrator = orchestrator;
        s_timeProvider = timeProvider;
        StartTime = timeProvider.GetUtcNow().UtcDateTime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Starting daemon: AAEmu.Game");
        Models.Game.StreamAoi.StreamAoiTable.ReplaceConfig(AppConfiguration.Instance.StreamAoi);

        // Check for updates
        using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_game", AppConfiguration.Instance.Connections.MySQLProvider.Database,
                    AppConfiguration.Instance.Connections.AutoApplyUpdates))
            {
                Logger.Fatal("Failed to update database!");
                Logger.Fatal("Press Ctrl+C to quit");
                return;
            }
        }

        ClientFileManager.Initialize();
        if (ClientFileManager.Sources.Count == 0)
        {
            Logger.Fatal($"Failed up load client files! ({string.Join(", ", AppConfiguration.Instance.ClientData.Sources)})");
            Logger.Fatal("Press Ctrl+C to quit");
            return;
        }

        var stopWatch = new Stopwatch();
        stopWatch.Start();

        // --- Auto-Attack system: ensure compact.sqlite3 has the 6 weapon-anim columns ---
        // The shipped ArcheAge compact.sqlite3 already contains these. This is a defensive
        // check for stripped/custom DBs and is a no-op if the columns are already present.
        Utils.DB.HoldablesSchemaCheck.EnsureColumns();

        // --- ID managers ---
        // All ID managers implement ILoadable and are handled by the orchestrator in Stage 2.
        // SkillTlIdManager.Instance.Initialize(); // static class, not migrated

        // --- Stage 2: Orchestrated parallel Load() ---
        // Managers implementing ILoadable are sorted by constructor dep graph and run in parallel batches.
        await _orchestrator.RunLoadAsync();

        // --- Stage 3: Post-load special steps ---
        GameDataManager.Instance.PostLoadGameData();
        CashShopManager.Instance.EnabledShop();

        // --- Scripts ---
        if (AppConfiguration.Instance.Scripts.LoadStrategy == ScriptsConfig.LoadStrategyType.Compilation)
        {
            ScriptCompiler.Compile();
        }
        else
        {
            // (Preferred for debugging)
            // Use reflection to load scripts
            ScriptReflector.Reflect();
        }

        TaskManager.Instance.Start();

        // --- Stage 4: Orchestrated parallel Initialize() ---
        await _orchestrator.RunInitializeAsync();

        // --- Stage 5: World creation + network ---
        // Start main_world and other static instances
        WorldManager.Instance.CreateStaticInstances();
        WorldManager.Instance.Initialize();

        CharacterManager.Instance.CheckForDeletedCharacters();
        CharacterManager.Instance.StartOnlineTracking();

        GameNetwork.Instance.Start();
        StreamNetwork.Instance.Start();
        LoginNetwork.Instance.Start();

        // Keep this independent from TaskManager: initial world/doodad work can saturate its queue long
        // enough for the 10.0.2.13 client's game-channel receive watchdog to expire. The native server's
        // ping scheduler is likewise a network timer, and advances every 1000 ms.
        var gamePingTask = new GamePingTask();
        _gameKeepaliveTimer = new Timer(
            _ =>
            {
                try
                {
                    gamePingTask.Execute();
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Game-channel keepalive tick failed");
                }
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        // Mirror interest: AOI cull + flush pending UnitStates (retail; not 1/s drip).
        TaskManager.Instance.Schedule(new MirrorSpawnStreamTask(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));

        // MirrorMovementStreamTask (synthetic idle SCUnitMovements self-stands) is INTENTIONALLY NOT scheduled.
        // Verified 2026-07-19: those stands cause clean System:Quit; Ping/Pong alone keeps the channel alive.
        // Commercial movement comes from real ZWUnitMovements (0x08) for units that actually move — re-enable
        // that relay separately (see MovementRelay / AAEMU_DISABLE_ZONE_MOVE_RELAY).

        stopWatch.Stop();
        Logger.Info($"Server started! Took {stopWatch.Elapsed}");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Stopping daemon...");

        await SaveManager.Instance.StopAsync();

        if (_gameKeepaliveTimer != null)
        {
            await _gameKeepaliveTimer.DisposeAsync();
            _gameKeepaliveTimer = null;
        }

        // SpawnManager.Instance.Stop(); Moved to World Instance
        await TaskManager.Instance.StopAsync(cancellationToken);
        GameNetwork.Instance.Stop();
        StreamNetwork.Instance.Stop();
        LoginNetwork.Instance.Stop();

        /*
        HousingManager.Instance.Save();
        MailManager.Instance.Save();
        ItemManager.Instance.Save();
        */
        WorldManager.Instance.Stop();

        TickManager.Instance.Stop();
        ClientFileManager.ClearSources();
    }

    public void Dispose()
    {
        Logger.Info("Disposing...");

        LogManager.Flush();
    }
}
