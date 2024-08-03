using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Starting all Id managers
        await Task.WhenAll([
            Task.Run(() => TaskIdManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => ObjectIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => TradeIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ContainerIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ItemIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => DoodadIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => CharacterIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => FamilyIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ExpeditionIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => VisitedSubZoneIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => PrivateBookIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => FriendIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => MateIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => HousingIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => HousingTldManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => TeamIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => QuestIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => MailIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => UccIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => MusicIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ShipyardIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => WorldIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => TlIdManager.Instance.Initialize(), cancellationToken),
            // SkillTlIdManager.Instance.Initialize();
        ]);


        await Task.WhenAll([
            Task.Run(() => TickManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => TaskManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => FeaturesManager.Initialize(), cancellationToken),
            Task.Run(() => LocalizationManager.Instance.Load(), cancellationToken),
            Task.Run(() => ZoneManager.Instance.Load(), cancellationToken),
            Task.Run(() => WorldManager.Instance.Load(), cancellationToken),
        ]);

        var heightmapTask = Task.Run(() => WorldManager.Instance.LoadHeightmaps(), cancellationToken);
        var waterBodyTask = Task.Run(() => WorldManager.Instance.LoadWaterBodies(), cancellationToken);

        await Task.WhenAll([
            Task.Run(() => ChatManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ShipyardManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => IndunManager.Instance.Initialize(), cancellationToken),

            Task.Run(() => GameDataManager.Instance.LoadGameData(), cancellationToken),
            Task.Run(() => QuestManager.Instance.Load(), cancellationToken),

            Task.Run(() => SphereQuestManager.Instance.Load(), cancellationToken),
            Task.Run(() => SphereQuestManager.Instance.Initialize(), cancellationToken),

            Task.Run(() => FormulaManager.Instance.Load(), cancellationToken),
            Task.Run(() => ExperienceManager.Instance.Load(), cancellationToken),
            Task.Run(() => AiPathsManager.Instance.Load(), cancellationToken),
        ]);

        await Task.WhenAll([
            Task.Run(() => SpecialtyManager.Instance.Load(), cancellationToken),
            Task.Run(() =>
            {
                ItemManager.Instance.Load();
                ItemManager.Instance.LoadUserItems();
            }, cancellationToken),
            Task.Run(() => AnimationManager.Instance.Load(), cancellationToken),
            Task.Run(() => PlotManager.Instance.Load(), cancellationToken),
            Task.Run(() => SkillManager.Instance.Load(), cancellationToken),
            Task.Run(() => CraftManager.Instance.Load(), cancellationToken),
            Task.Run(() => MateManager.Instance.Load(), cancellationToken),
            Task.Run(() => SlaveManager.Instance.Load(), cancellationToken),
            Task.Run(() => TeamManager.Instance.Load(), cancellationToken),
            Task.Run(() => AuctionManager.Instance.Load(), cancellationToken),
            Task.Run(() => MailManager.Instance.Load(), cancellationToken),
            Task.Run(() => ExpressTextManager.Instance.Load(), cancellationToken),

            Task.Run(() => NameManager.Instance.Load(), cancellationToken),
            Task.Run(() => FactionManager.Instance.Load(), cancellationToken),
            Task.Run(() => ExpeditionManager.Instance.Load(), cancellationToken),
            Task.Run(() => CharacterManager.Instance.Load(), cancellationToken),
            Task.Run(() => FamilyManager.Instance.Load(), cancellationToken),
            Task.Run(() => PortalManager.Instance.Load(), cancellationToken),
            Task.Run(() => FriendMananger.Instance.Load(), cancellationToken),
            Task.Run(() => ModelManager.Instance.Load(), cancellationToken),
            Task.Run(() => AIManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => GameScheduleManager.Instance.Load(), cancellationToken),
        ]);

        await Task.WhenAll([
            Task.Run(() => NpcManager.Instance.Load(), cancellationToken),
            Task.Run(() => DoodadManager.Instance.Load(), cancellationToken),
        ]);

        await Task.WhenAll([
            Task.Run(() => SpawnManager.Instance.Load(), cancellationToken),
            Task.Run(() => TaxationsManager.Instance.Load(), cancellationToken),
            Task.Run(() => HousingManager.Instance.Load(), cancellationToken),
            Task.Run(() => TransferManager.Instance.Load(), cancellationToken),
            Task.Run(() => GimmickManager.Instance.Load(), cancellationToken),
            Task.Run(() => ShipyardManager.Instance.Load(), cancellationToken),

            Task.Run(() => SubZoneManager.Instance.Load(), cancellationToken),
            Task.Run(() => PublicFarmManager.Instance.Load(), cancellationToken),
            Task.Run(() => AccessLevelManager.Instance.Load(), cancellationToken),
            Task.Run(() => CashShopManager.Instance.Load(), cancellationToken),
            Task.Run(() => CashShopManager.Instance.EnabledShop(), cancellationToken),
            Task.Run(() => UccManager.Instance.Load(), cancellationToken),
            Task.Run(() => MusicManager.Instance.Load(), cancellationToken),
            Task.Run(() => AiGeoDataManager.Instance.Load(), cancellationToken),
            Task.Run(() => {
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
            }),
            Task.Run(() => TimeManager.Instance.Start(), cancellationToken),
            Task.Run(() => TaskManager.Instance.Start(), cancellationToken),
            // LaborPowerManager.Initialize();
            Task.Run(() => TimedRewardsManager.Instance.Initialize(), cancellationToken),

            Task.Run(() => DuelManager.Initialize(), cancellationToken),
            Task.Run(() => SaveManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => AreaTriggerManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => SpecialtyManager.Initialize(), cancellationToken),
            Task.Run(() => TransferManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => GimmickManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => SlaveManager.Initialize(), cancellationToken),
            Task.Run(() => CashShopManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => GameDataManager.Instance.PostLoadGameData(), cancellationToken),
            Task.Run(() => FishSchoolManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => RadarManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => PublicFarmManager.Instance.Initialize(), cancellationToken),
        ]);

        if ((waterBodyTask != null) && (!waterBodyTask.IsCompleted))
        {
            Logger.Info("Waiting on water to be loaded before proceeding, please wait ...");
            await waterBodyTask;
        }

        if ((heightmapTask != null) && (!heightmapTask.IsCompleted))
        {
            Logger.Info("Waiting on heightmaps to be loaded before proceeding, please wait ...");
            await heightmapTask;
        }


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
}
