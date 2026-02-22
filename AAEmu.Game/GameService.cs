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
using AAEmu.Game.Utils.Scripts;

using Microsoft.Extensions.Hosting;

using NLog;

namespace AAEmu.Game;

public sealed class GameService : IHostedService, IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public static DateTime StartTime { get; private set; } = DateTime.UtcNow;
    public static TimeSpan TimeSinceStart => DateTime.UtcNow.Subtract(StartTime);

    private readonly ManagerOrchestrator _orchestrator;

    public GameService(IServiceProvider serviceProvider, ManagerOrchestrator orchestrator)
    {
        SingletonContainer.ServiceProvider = serviceProvider;
        _orchestrator = orchestrator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Starting daemon: AAEmu.Game");

        // Check for updates
        using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_game", AppConfiguration.Instance.Connections.MySQLProvider.Database))
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

        // --- World base (explicit: needed before all other Load() calls) ---
        WorldIdManager.Instance.Initialize();
        WorldManager.Instance.Load();
        FeaturesManager.Initialize();

        // --- ID managers ---
        // All ID managers implement ILoadable and are handled by the orchestrator in Stage 2.
        // SkillTlIdManager.Instance.Initialize(); // static class, not migrated

        // --- Stage 1: Pre-load special steps ---
        // TODO: Implement lazy loading for heightmaps
        var heightmapTask = Task.Run(WorldManager.Instance.LoadHeightmaps, cancellationToken);

        // --- Stage 2: Orchestrated parallel Load() ---
        // Managers implementing ILoadable are sorted by constructor dep graph and run in parallel batches.
        await _orchestrator.RunLoadAsync();

        // --- Stage 3: Post-load special steps ---
        GameDataManager.Instance.PostLoadGameData();
        ItemManager.Instance.LoadUserItems();
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

        TimeManager.Instance.Start();
        TaskManager.Instance.Start();

        // --- Stage 4: Orchestrated parallel Initialize() ---
        await _orchestrator.RunInitializeAsync();

        // --- Stage 5: World creation + network ---
        if (heightmapTask != null && !heightmapTask.IsCompleted)
        {
            Logger.Info("Waiting on heightmaps to be loaded before proceeding, please wait ...");
            await heightmapTask;
        }

        // Start main_world and other static instances
        WorldManager.Instance.CreateStaticInstances();
        WorldManager.Instance.Initialize();

        CharacterManager.Instance.CheckForDeletedCharacters();
        CharacterManager.Instance.StartOnlineTracking();

        GameNetwork.Instance.Start();
        StreamNetwork.Instance.Start();
        LoginNetwork.Instance.Start();

        stopWatch.Stop();
        Logger.Info($"Server started! Took {stopWatch.Elapsed}");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.Info("Stopping daemon...");

        await SaveManager.Instance.StopAsync();

        // SpawnManager.Instance.Stop(); Moved to World Instance
        TaskManager.Instance.Stop();
        GameNetwork.Instance.Stop();
        StreamNetwork.Instance.Stop();
        LoginNetwork.Instance.Stop();

        /*
        HousingManager.Instance.Save();
        MailManager.Instance.Save();
        ItemManager.Instance.Save();
        */
        AIManager.Instance.Stop();
        WorldManager.Instance.Stop();

        TickManager.Instance.Stop();
        TimeManager.Instance.Stop();

        ClientFileManager.ClearSources();
    }

    public void Dispose()
    {
        Logger.Info("Disposing...");

        LogManager.Flush();
    }
}
