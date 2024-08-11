using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Utils.DB;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
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

        // Required for level 0
        await Task.WhenAll(FireInParallel([
            // Id managers
            () => TaskIdManager.Instance.Initialize(),
            () => ObjectIdManager.Instance.Initialize(),
            () => TradeIdManager.Instance.Initialize(),
            () => ContainerIdManager.Instance.Initialize(),
            () => ItemIdManager.Instance.Initialize(),
            () => DoodadIdManager.Instance.Initialize(),
            () => CharacterIdManager.Instance.Initialize(),
            () => FamilyIdManager.Instance.Initialize(),
            () => ExpeditionIdManager.Instance.Initialize(),
            () => VisitedSubZoneIdManager.Instance.Initialize(),
            () => PrivateBookIdManager.Instance.Initialize(),
            () => FriendIdManager.Instance.Initialize(),
            () => MateIdManager.Instance.Initialize(),
            () => HousingIdManager.Instance.Initialize(),
            () => HousingTldManager.Instance.Initialize(),
            () => TeamIdManager.Instance.Initialize(),
            () => QuestIdManager.Instance.Initialize(),
            () => MailIdManager.Instance.Initialize(),
            () => UccIdManager.Instance.Initialize(),
            () => MusicIdManager.Instance.Initialize(),
            () => ShipyardIdManager.Instance.Initialize(),
            () => WorldIdManager.Instance.Initialize(),
            () => TlIdManager.Instance.Initialize(),

            // Non-Id managers
            () => FormulaManager.Instance.Load(),
            () => {
                PlotManager.Instance.Load();
                SkillManager.Instance.Load();
            }
            // SkillTlIdManager.Instance.Initialize();
        ], cancellationToken));

        // Independent running managers (level 0), no dependents at all
        var independentLevel0 = FireInParallel([
            () => // Load Scripts
            {
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
            },
            () => FeaturesManager.Initialize(),
            () => ExperienceManager.Instance.Load(),
            () => AiPathsManager.Instance.Load(),
            () => ChatManager.Instance.Initialize(),
            () => PublicFarmManager.Instance.Load(),
            () => AccessLevelManager.Instance.Load(),
            () => UccManager.Instance.Load(),
            () => MusicManager.Instance.Load(),
            () => TimeManager.Instance.Start(),
            () => DuelManager.Initialize(),
            () => TeamManager.Instance.Load(),
            () => ExpressTextManager.Instance.Load(),
            () => NameManager.Instance.Load(),
            () => ExpeditionManager.Instance.Load(),
            () => FamilyManager.Instance.Load(),
            () => FriendManager.Instance.Load(),
            () => ModelManager.Instance.Load(),
            () => NpcManager.Instance.Load(),
            () => CraftManager.Instance.Load(),
            () => MateManager.Instance.Load(),
            () => AnimationManager.Instance.Load(),
        ], cancellationToken);

        // Required for level 1
        await Task.WhenAll(FireInParallel([
            () => TickManager.Instance.Initialize(),
            () => TaskManager.Instance.Initialize(),
            () => LocalizationManager.Instance.Load(),
            () => GimmickManager.Instance.Load(),
            () => ItemManager.Instance.Load(),
            () => TaxationsManager.Instance.Load(),
            () => ZoneManager.Instance.Load(),
            () => SlaveManager.Instance.Load()
        ], cancellationToken));

        // Long running LoadGameData and direct dependencies
        var loadGameDataTasks = Task.Run(() =>
        {
            GameDataManager.Instance.LoadGameData();
            DoodadManager.Instance.Load(); // Only depends on GameDataManager
            GameDataManager.Instance.PostLoadGameData();
        }, cancellationToken);

        // Independent running managers (level 1)
        // Depends on the previous ones, but do not have any dependents and can run without waiting)
        var independentLevel1 = FireInParallel([
            () => ItemManager.Instance.LoadUserItems(),
            () => RadarManager.Instance.Initialize(),
            () => AIManager.Instance.Initialize(),
            () => IndunManager.Instance.Initialize(),
            () => PortalManager.Instance.Load(),
            () => FactionManager.Instance.Load(),
            () => TaskManager.Instance.Start(),
            () => GimmickManager.Instance.Initialize(),
            () => PublicFarmManager.Instance.Initialize(),
            () => TimedRewardsManager.Instance.Initialize(),
        ], cancellationToken);

        // Required for level 2
        await Task.WhenAll(FireInParallel([
            () => WorldManager.Instance.Load(),
            () => ZoneManager.Instance.Load(),
            () => SlaveManager.Instance.Load(),
            () => QuestManager.Instance.Load(),
            () => ShipyardManager.Instance.Initialize(),
            () => MailManager.Instance.Load(),
            () => AuctionManager.Instance.Load(),
            () => SpecialtyManager.Instance.Load()
        ], cancellationToken));

        // Independent running managers (level 2)
        var independentLevel2 = FireInParallel([
            () => HousingManager.Instance.Load(),
            () => AreaTriggerManager.Instance.Initialize(),
            () => SubZoneManager.Instance.Load(),
            () => WorldManager.Instance.LoadHeightmaps(),
            () => WorldManager.Instance.LoadWaterBodies(),
            () => AiGeoDataManager.Instance.Load(),
            () => SlaveManager.Initialize(),
            () => GameScheduleManager.Instance.Load(),
            () => {
                SphereQuestManager.Instance.Load();
                SphereQuestManager.Instance.Initialize();
            },
            () => SaveManager.Instance.Initialize(),
            () => FishSchoolManager.Instance.Initialize(),
            () => ShipyardManager.Instance.Load(),
            () => SpecialtyManager.Initialize(),
            () => {
                CashShopManager.Instance.Load();
                CashShopManager.Instance.EnabledShop();
                CashShopManager.Instance.Initialize();
            },
            () => {
                TransferManager.Instance.Load();
                TransferManager.Instance.Initialize();
            }
        ], cancellationToken);

        await loadGameDataTasks;

        // Run all tasks that depends on loadGameDataTasks
        var independentAfterGameData = FireInParallel([
            () => SpawnManager.Instance.Load(),
            () => CharacterManager.Instance.Load(),
        ], cancellationToken);

        // All independent managers should be loaded after this...
        await Task.WhenAll(
            independentAfterGameData
            .Concat(independentLevel0)
            .Concat(independentLevel1)
            .Concat(independentLevel2));

        var spawnSw = new Stopwatch();
        Logger.Info("Spawning units...");
        spawnSw.Start();
        HousingManager.Instance.SpawnAll(); // Houses need to be spawned before doodads
        SpawnManager.Instance.SpawnAll();
        TransferManager.Instance.SpawnAll();
        spawnSw.Stop();
        Logger.Info($"Units spawned in {spawnSw.Elapsed}");

        // Start running Physics when everything is loaded
        WorldManager.Instance.StartPhysics();

        CharacterManager.CheckForDeletedCharacters();
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

        SpawnManager.Instance.Stop();
        TaskManager.Instance.Stop();
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
        TimeManager.Instance.Stop();

        ClientFileManager.ClearSources();
    }

    public void Dispose()
    {
        Logger.Info("Disposing...");

        LogManager.Flush();
    }

    private List<Task> FireInParallel(IEnumerable<Action> actions, CancellationToken cancellationToken)
    {
        List<Task> tasks = [];
        foreach (var action in actions)
        {
            tasks.Add(Task.Run(action, cancellationToken));
        }

        return tasks;
    }
}
