using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Cryptography;
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

        TickManager.Instance.Initialize();
        TaskIdManager.Instance.Initialize();
        TaskManager.Instance.Initialize();

        WorldManager.Instance.Load();
        WorldIdManager.Instance.Initialize();
        FeaturesManager.Initialize();

        LocalizationManager.Instance.Load();
        ObjectIdManager.Instance.Initialize();
        TradeIdManager.Instance.Initialize();

        ZoneManager.Instance.Load();
        var heightmapTask = Task.Run(() =>
        {
            WorldManager.Instance.LoadHeightmaps();
        }, cancellationToken);

        var waterBodyTask = Task.Run(() =>
        {
            WorldManager.Instance.LoadWaterBodies();
        }, cancellationToken);

        ContainerIdManager.Instance.Initialize();
        ItemIdManager.Instance.Initialize();
        DoodadIdManager.Instance.Initialize();
        ChatManager.Instance.Initialize();
        CharacterIdManager.Instance.Initialize();
        FamilyIdManager.Instance.Initialize();
        ExpeditionIdManager.Instance.Initialize();
        VisitedSubZoneIdManager.Instance.Initialize();
        PrivateBookIdManager.Instance.Initialize();
        FriendIdManager.Instance.Initialize();
        MateIdManager.Instance.Initialize();
        HousingIdManager.Instance.Initialize();
        HousingTldManager.Instance.Initialize();
        TeamIdManager.Instance.Initialize();
        QuestIdManager.Instance.Initialize();
        MailIdManager.Instance.Initialize();
        UccIdManager.Instance.Initialize();
        MusicIdManager.Instance.Initialize();
        ShipyardIdManager.Instance.Initialize();
        ShipyardManager.Instance.Initialize();
        // SkillTlIdManager.Instance.Initialize();
        IndunManager.Instance.Initialize();
        AuctionIdManager.Instance.Initialize();

        GameDataManager.Instance.LoadGameData();
        QuestManager.Instance.Load();

        SphereQuestManager.Instance.Load();
        SphereQuestManager.Instance.Initialize();

        //ResidentManager.Instance.Initialize();
        ResidentManager.Instance.Load();
        //AttendanceManager.Instance.Load();

        FormulaManager.Instance.Load();
        ExperienceManager.Instance.Load();
        AiPathsManager.Instance.Load();

        TlIdManager.Instance.Initialize();
        SpecialtyManager.Instance.Load();
        ItemManager.Instance.Load();
        ItemManager.Instance.LoadUserItems();
        AnimationManager.Instance.Load();
        PlotManager.Instance.Load();
        SkillManager.Instance.Load();
        CraftManager.Instance.Load();
        MateManager.Instance.Load();
        SlaveManager.Instance.Load();
        TeamManager.Instance.Load();
        AuctionManager.Instance.Load();
        MailManager.Instance.Load();
        ExpressTextManager.Instance.Load();

        NameManager.Instance.Load();
        FactionManager.Instance.Load();
        ExpeditionManager.Instance.Load();
        CharacterManager.Instance.Load();
        FamilyManager.Instance.Load();
        PortalManager.Instance.Load();
        FriendMananger.Instance.Load();
        ModelManager.Instance.Load();

        AIManager.Instance.Initialize();

        GameScheduleManager.Instance.Load();
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
        EncryptionManager.Instance.Load();

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
        ManaRegenManager.Instance.Initialize();
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
}
