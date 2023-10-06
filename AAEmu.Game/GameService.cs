using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils.Updater;
using AAEmu.Game.IO;
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
using AAEmu.Game.Models;
using AAEmu.Game.Utils.Scripts;
using Microsoft.Extensions.Hosting;
using NLog;
using AAEmu.Commons.Utils.DB;

namespace AAEmu.Game;

public sealed class GameService : IHostedService, IDisposable
{
    private static Logger _logger = LogManager.GetCurrentClassLogger();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Starting daemon: AAEmu.Game");

        // Check for updates
        using (var connection = MySQL.CreateConnection())
        {
            if (!MySqlDatabaseUpdater.Run(connection, "aaemu_game", AppConfiguration.Instance.Connections.MySQLProvider.Database))
            {
                _logger.Fatal("Failed to update database!");
                _logger.Fatal("Press Ctrl+C to quit");
                return;
            }
        }

        ClientFileManager.Initialize();
        if (ClientFileManager.ListSources().Count == 0)
        {
            _logger.Fatal($"Failed up load client files! ({string.Join(", ", AppConfiguration.Instance.ClientData.Sources)})");
            _logger.Fatal("Press Ctrl+C to quit");
            return;
        }

        var stopWatch = new Stopwatch();

        stopWatch.Start();

        TickManager.Instance.Initialize();
        TaskIdManager.Instance.Initialize();
        TaskManager.Instance.Initialize();

        WorldManager.Instance.Load();
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
        LaborPowerManager.Initialize();
        QuestIdManager.Instance.Initialize();
        MailIdManager.Instance.Initialize();
        UccIdManager.Instance.Initialize();
        MusicIdManager.Instance.Initialize();
        ShipyardIdManager.Instance.Initialize();
        ShipyardManager.Instance.Initialize();

        GameDataManager.Instance.LoadGameData();
        QuestManager.Instance.Load();

        SphereQuestManager.Instance.Load();
        SphereQuestManager.Instance.Initialize();

        FormulaManager.Instance.Load();
        ExperienceManager.Instance.Load();

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

        SpawnManager.Instance.Load();

        AccessLevelManager.Instance.Load();
        CashShopManager.Instance.Load();
        UccManager.Instance.Load();
        MusicManager.Instance.Load();
        AiGeoDataManager.Instance.Load();

        ScriptCompiler.Compile();

        TimeManager.Instance.Start();
        TaskManager.Instance.Start();

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

        if ((waterBodyTask != null) && (!waterBodyTask.IsCompleted))
        {
            _logger.Info("Waiting on water to be loaded before proceeding, please wait ...");
            await waterBodyTask;
        }

        if ((heightmapTask != null) && (!heightmapTask.IsCompleted))
        {
            _logger.Info("Waiting on heightmaps to be loaded before proceeding, please wait ...");
            await heightmapTask;
        }

        var spawnSw = new Stopwatch();
        _logger.Info("Spawning units...");
        spawnSw.Start();
        HousingManager.Instance.SpawnAll(); // Houses need to be spawned before doodads
        SpawnManager.Instance.SpawnAll();
        TransferManager.Instance.SpawnAll();
        spawnSw.Stop();
        _logger.Info("Units spawned in {0}", spawnSw.Elapsed);

        // Start running Physics when everything is loaded
        WorldManager.Instance.StartPhysics();

        CharacterManager.CheckForDeletedCharacters();

        GameNetwork.Instance.Start();
        StreamNetwork.Instance.Start();
        LoginNetwork.Instance.Start();

        stopWatch.Stop();
        _logger.Info("Server started! Took {0}", stopWatch.Elapsed);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Stopping daemon...");

        SaveManager.Instance.Stop();

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
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logger.Info("Disposing...");

        LogManager.Flush();
    }
}
