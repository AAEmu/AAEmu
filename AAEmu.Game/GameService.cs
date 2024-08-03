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

        await Task.WhenAll([
            Task.Run(() => TickManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => TaskIdManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => TaskManager.Instance.Initialize(),cancellationToken),
            Task.Run(() => FeaturesManager.Initialize(), cancellationToken),
            Task.Run(() => LocalizationManager.Instance.Load(), cancellationToken),
            Task.Run(() => ObjectIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => TradeIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ZoneManager.Instance.Load(), cancellationToken),
            Task.Run(() =>
            {
                WorldManager.Instance.Load();
                WorldIdManager.Instance.Initialize();
            },cancellationToken),
        ]);

        var heightmapTask = Task.Run(() => WorldManager.Instance.LoadHeightmaps(), cancellationToken);
        var waterBodyTask = Task.Run(() => WorldManager.Instance.LoadWaterBodies(), cancellationToken);

        await Task.WhenAll([
            Task.Run(() => ContainerIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => ItemIdManager.Instance.Initialize(), cancellationToken),
            Task.Run(() => DoodadIdManager.Instance.Initialize(), cancellationToken),
        ]);

        await Task.WhenAll([
            Task.Run(() => ChatManager.Instance.Initialize(), cancellationToken),
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
        ]);

        await Task.WhenAll([
            Task.Run(() => ChatManager.Instance.Initialize(), cancellationToken),
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
        ]);

        await Task.WhenAll([
            Task.Run(() => ShipyardManager.Instance.Initialize(), cancellationToken),
        // SkillTlIdManager.Instance.Initialize();
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
            Task.Run(() => TlIdManager.Instance.Initialize(), cancellationToken),
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

        NpcManager.Instance.Load();

        DoodadManager.Instance.Load();
        TaxationsManager.Instance.Load();
        HousingManager.Instance.Load();
        TransferManager.Instance.Load();
        GimmickManager.Instance.Load();
        ShipyardManager.Instance.Load();

        SubZoneManager.Instance.Load();
        PublicFarmManager.Instance.Load();

        SpawnManager.Instance.Load();

        AccessLevelManager.Instance.Load();
        CashShopManager.Instance.Load();
        CashShopManager.Instance.EnabledShop();
        UccManager.Instance.Load();
        MusicManager.Instance.Load();
        AiGeoDataManager.Instance.Load();

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
        // LaborPowerManager.Initialize();
        TimedRewardsManager.Instance.Initialize();

        DuelManager.Initialize();
        SaveManager.Instance.Initialize();
        AreaTriggerManager.Instance.Initialize();
        SpecialtyManager.Initialize();
        TransferManager.Instance.Initialize();
        GimmickManager.Instance.Initialize();
        SlaveManager.Initialize();
        CashShopManager.Instance.Initialize();
        GameDataManager.Instance.PostLoadGameData();
        FishSchoolManager.Instance.Initialize();
        RadarManager.Instance.Initialize();
        PublicFarmManager.Instance.Initialize();

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
