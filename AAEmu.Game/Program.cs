using System.Reflection;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Services;
using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Utils.Scripts;

using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NLog;
using NLog.Config;
using OSVersionExtension;

namespace AAEmu.Game;

public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly Thread _thread = Thread.CurrentThread;
    private static DateTime _startTime;
    private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";
    public static AutoResetEvent ShutdownSignal => new(false); // TODO save to shutdown server?

    public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;
    private static string[] _launchArgs;

    public static async Task<int> Main(string[] args)
    {
        _launchArgs = args;
        Initialization();

        if (args.Length > 0 && args[0] == "compiler-check")
        {
            Logger.Info("Check compilation");
            var result = ScriptCompiler.CompileScriptsWithAllDependencies(out _, out var diagnostics);

            if (result)
            {
                Logger.Info("Compilation successful");
                return 0;
            }
            else
            {
                Logger.Error(new CompilationErrorException("Compilation failed", diagnostics), "Compilation failed");
                return 1;
            }
        }

        var configurationRoot = BuildConfiguration(args);

        // Apply MySQL Configuration
        var preBootConfig = new AppConfiguration();
        configurationRoot.Bind(preBootConfig);
        MySQL.SetConfiguration(preBootConfig.Connections.MySQLProvider);

        try
        {
            // Test the DB connection
            var connection = MySQL.CreateConnection();
            connection.Close();
            connection.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "MySQL connection failed, check your configuration!");
            LogManager.Flush();
            return 1;
        }

        try
        {
            // Test the DB connection
            using var connection = SQLite.CreateConnection();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "Failed to load compact.sqlite3 database check if it exists!");
            LogManager.Flush();
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var builder = new HostBuilder()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddEnvironmentVariables();

                if (args != null)
                {
                    config.AddCommandLine(args);
                }
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions();
                services.Configure<AppConfiguration>(configurationRoot);
                services.AddSingleton(TimeProvider.System);

                // -- Hosted services --
                services.AddSingleton<IHostedService, GameService>();
                services.AddSingleton<IHostedService, WebApiService>();
                services.AddSingleton<IHostedService, DiscordBotService>();

                // -- Singleton<T>-based managers (AAEmu.Game.Core.Managers) --
                services.AddSingleton<AccessLevelManager>();
                services.AddSingleton<IAccessLevelManager>(sp => sp.GetRequiredService<AccessLevelManager>());

                services.AddSingleton<AccountManager>();
                services.AddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>());

                services.AddSingleton<AIManager>();
                services.AddSingleton<IAIManager>(sp => sp.GetRequiredService<AIManager>());

                services.AddSingleton<AiPathsManager>();
                services.AddSingleton<IAiPathsManager>(sp => sp.GetRequiredService<AiPathsManager>());

                services.AddSingleton<AnimationManager>();
                services.AddSingleton<IAnimationManager>(sp => sp.GetRequiredService<AnimationManager>());

                services.AddSingleton<AuctionManager>();
                services.AddSingleton<IAuctionManager>(sp => sp.GetRequiredService<AuctionManager>());

                services.AddSingleton<CashShopManager>();
                services.AddSingleton<ICashShopManager>(sp => sp.GetRequiredService<CashShopManager>());

                services.AddSingleton<ChatManager>();
                services.AddSingleton<IChatManager>(sp => sp.GetRequiredService<ChatManager>());

                services.AddSingleton<CommandManager>();
                services.AddSingleton<ICommandManager>(sp => sp.GetRequiredService<CommandManager>());

                services.AddSingleton<CraftManager>();
                services.AddSingleton<ICraftManager>(sp => sp.GetRequiredService<CraftManager>());

                services.AddSingleton<CrimeManager>();
                services.AddSingleton<ICrimeManager>(sp => sp.GetRequiredService<CrimeManager>());

                services.AddSingleton<DuelManager>();
                services.AddSingleton<IDuelManager>(sp => sp.GetRequiredService<DuelManager>());

                services.AddSingleton<EffectTaskManager>();
                services.AddSingleton<IEffectTaskManager>(sp => sp.GetRequiredService<EffectTaskManager>());

                services.AddSingleton<ExpeditionManager>();
                services.AddSingleton<IExpeditionManager>(sp => sp.GetRequiredService<ExpeditionManager>());

                services.AddSingleton<ExperienceManager>();
                services.AddSingleton<IExperienceManager>(sp => sp.GetRequiredService<ExperienceManager>());

                services.AddSingleton<ExpressTextManager>();
                services.AddSingleton<IExpressTextManager>(sp => sp.GetRequiredService<ExpressTextManager>());

                services.AddSingleton<FamilyManager>();
                services.AddSingleton<IFamilyManager>(sp => sp.GetRequiredService<FamilyManager>());

                services.AddSingleton<FeaturesManager>();
                services.AddSingleton<IFeaturesManager>(sp => sp.GetRequiredService<FeaturesManager>());

                services.AddSingleton<FishSchoolManager>();
                services.AddSingleton<IFishSchoolManager>(sp => sp.GetRequiredService<FishSchoolManager>());

                services.AddSingleton<FormulaManager>();
                services.AddSingleton<IFormulaManager>(sp => sp.GetRequiredService<FormulaManager>());

                services.AddSingleton<FriendMananger>();
                services.AddSingleton<IFriendManager>(sp => sp.GetRequiredService<FriendMananger>());

                services.AddSingleton<GameScheduleManager>();
                services.AddSingleton<IGameScheduleManager>(sp => sp.GetRequiredService<GameScheduleManager>());

                services.AddSingleton<HousingManager>();
                services.AddSingleton<IHousingManager>(sp => sp.GetRequiredService<HousingManager>());

                services.AddSingleton<IndunManager>();
                services.AddSingleton<IIndunManager>(sp => sp.GetRequiredService<IndunManager>());

                services.AddSingleton<InstantGameManager>();
                services.AddSingleton<IInstantGameManager>(sp => sp.GetRequiredService<InstantGameManager>());

                services.AddSingleton<ItemManager>();
                services.AddSingleton<IItemManager>(sp => sp.GetRequiredService<ItemManager>());

                services.AddSingleton<LocalizationManager>();
                services.AddSingleton<ILocalizationManager>(sp => sp.GetRequiredService<LocalizationManager>());

                services.AddSingleton<MailManager>();
                services.AddSingleton<IMailManager>(sp => sp.GetRequiredService<MailManager>());

                services.AddSingleton<ManaRegenManager>();
                services.AddSingleton<IManaRegenManager>(sp => sp.GetRequiredService<ManaRegenManager>());

                services.AddSingleton<ModelManager>();
                services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ModelManager>());

                services.AddSingleton<MusicManager>();
                services.AddSingleton<IMusicManager>(sp => sp.GetRequiredService<MusicManager>());

                services.AddSingleton<NameManager>();
                services.AddSingleton<INameManager>(sp => sp.GetRequiredService<NameManager>());

                services.AddSingleton<PlotManager>();
                services.AddSingleton<IPlotManager>(sp => sp.GetRequiredService<PlotManager>());

                services.AddSingleton<PortalManager>();
                services.AddSingleton<IPortalManager>(sp => sp.GetRequiredService<PortalManager>());

                services.AddSingleton<PublicFarmManager>();
                services.AddSingleton<IPublicFarmManager>(sp => sp.GetRequiredService<PublicFarmManager>());

                services.AddSingleton<QuestManager>();
                services.AddSingleton<IQuestManager>(sp => sp.GetRequiredService<QuestManager>());

                services.AddSingleton<RadarManager>();
                services.AddSingleton<IRadarManager>(sp => sp.GetRequiredService<RadarManager>());

                services.AddSingleton<SaveManager>();
                services.AddSingleton<ISaveManager>(sp => sp.GetRequiredService<SaveManager>());

                services.AddSingleton<ShipyardManager>();
                services.AddSingleton<IShipyardManager>(sp => sp.GetRequiredService<ShipyardManager>());

                services.AddSingleton<SkillManager>();
                services.AddSingleton<ISkillManager>(sp => sp.GetRequiredService<SkillManager>());

                services.AddSingleton<SubZoneManager>();
                services.AddSingleton<ISubZoneManager>(sp => sp.GetRequiredService<SubZoneManager>());

                services.AddSingleton<SusManager>();
                services.AddSingleton<ISusManager>(sp => sp.GetRequiredService<SusManager>());

                services.AddSingleton<TaskManager>();
                services.AddSingleton<ITaskManager>(sp => sp.GetRequiredService<TaskManager>());

                services.AddSingleton<TaxationsManager>();
                services.AddSingleton<ITaxationsManager>(sp => sp.GetRequiredService<TaxationsManager>());

                services.AddSingleton<TeamManager>();
                services.AddSingleton<ITeamManager>(sp => sp.GetRequiredService<TeamManager>());

                services.AddSingleton<TickManager>();
                services.AddSingleton<ITickManager>(sp => sp.GetRequiredService<TickManager>());

                services.AddSingleton<TimedRewardsManager>();
                services.AddSingleton<ITimedRewardsManager>(sp => sp.GetRequiredService<TimedRewardsManager>());

                services.AddSingleton<TimeManager>();
                services.AddSingleton<ITimeManager>(sp => sp.GetRequiredService<TimeManager>());

                services.AddSingleton<TradeManager>();
                services.AddSingleton<ITradeManager>(sp => sp.GetRequiredService<TradeManager>());

                // -- Singleton<T>-based managers (AAEmu.Game.Core.Managers.UnitManagers) --
                services.AddSingleton<CharacterManager>();
                services.AddSingleton<ICharacterManager>(sp => sp.GetRequiredService<CharacterManager>());

                services.AddSingleton<DoodadManager>();
                services.AddSingleton<IDoodadManager>(sp => sp.GetRequiredService<DoodadManager>());

                services.AddSingleton<NpcManager>();
                services.AddSingleton<INpcManager>(sp => sp.GetRequiredService<NpcManager>());

                services.AddSingleton<TrialManager>();
                services.AddSingleton<ITrialManager>(sp => sp.GetRequiredService<TrialManager>());

                services.AddSingleton<CrimeManager>();
                services.AddSingleton<ICrimeManager>(sp => sp.GetRequiredService<CrimeManager>());

                // -- Singleton<T>-based managers (AAEmu.Game.Core.Managers.World) --
                services.AddSingleton<AreaTriggerManager>();
                services.AddSingleton<IAreaTriggerManager>(sp => sp.GetRequiredService<AreaTriggerManager>());

                services.AddSingleton<EnterWorldManager>();
                services.AddSingleton<IEnterWorldManager>(sp => sp.GetRequiredService<EnterWorldManager>());

                services.AddSingleton<FactionManager>();
                services.AddSingleton<IFactionManager>(sp => sp.GetRequiredService<FactionManager>());

                services.AddSingleton<SpecialtyManager>();
                services.AddSingleton<ISpecialtyManager>(sp => sp.GetRequiredService<SpecialtyManager>());

                services.AddSingleton<StreamManager>();
                services.AddSingleton<IStreamManager>(sp => sp.GetRequiredService<StreamManager>());

                services.AddSingleton<WorldManager>();
                services.AddSingleton<IWorldManager>(sp => sp.GetRequiredService<WorldManager>());

                services.AddSingleton<ZoneManager>();
                services.AddSingleton<IZoneManager>(sp => sp.GetRequiredService<ZoneManager>());

                // -- Singleton<T>-based managers (AAEmu.Game.Core.Managers.Stream) --
                services.AddSingleton<UccManager>();
                services.AddSingleton<IUccManager>(sp => sp.GetRequiredService<UccManager>());

                // -- Singleton<T>-based managers (AAEmu.Game.GameData.Framework) --
                services.AddSingleton<GameDataManager>();
                services.AddSingleton<IGameDataManager>(sp => sp.GetRequiredService<GameDataManager>());

                // -- IdManagers (own static Instance, factory pattern) --
                services.AddSingleton<AuctionIdManager>();
                services.AddSingleton<IAuctionIdManager>(sp => sp.GetRequiredService<AuctionIdManager>());

                services.AddSingleton<ChatIdManager>();
                services.AddSingleton<IChatIdManager>(sp => sp.GetRequiredService<ChatIdManager>());

                services.AddSingleton<CharacterIdManager>();
                services.AddSingleton<ICharacterIdManager>(sp => sp.GetRequiredService<CharacterIdManager>());

                services.AddSingleton<ContainerIdManager>();
                services.AddSingleton<IContainerIdManager>(sp => sp.GetRequiredService<ContainerIdManager>());

                services.AddSingleton<DoodadIdManager>();
                services.AddSingleton<IDoodadIdManager>(sp => sp.GetRequiredService<DoodadIdManager>());

                services.AddSingleton<CrimeIdManager>();
                services.AddSingleton<ICrimeIdManager>(sp => sp.GetRequiredService<CrimeIdManager>());

                services.AddSingleton<TrialIdManager>();
                services.AddSingleton<ITrialIdManager>(sp => sp.GetRequiredService<ITrialIdManager>());

                services.AddSingleton<ExpeditionIdManager>();
                services.AddSingleton<IExpeditionIdManager>(sp => sp.GetRequiredService<ExpeditionIdManager>());

                services.AddSingleton<FamilyIdManager>();
                services.AddSingleton<IFamilyIdManager>(sp => sp.GetRequiredService<FamilyIdManager>());

                services.AddSingleton<FriendIdManager>();
                services.AddSingleton<IFriendIdManager>(sp => sp.GetRequiredService<FriendIdManager>());

                services.AddSingleton<GimmickIdManager>();
                services.AddSingleton<IGimmickIdManager>(sp => sp.GetRequiredService<GimmickIdManager>());

                services.AddSingleton<HousingIdManager>();
                services.AddSingleton<IHousingIdManager>(sp => sp.GetRequiredService<HousingIdManager>());

                services.AddSingleton<HousingTldManager>();
                services.AddSingleton<IHousingTldManager>(sp => sp.GetRequiredService<HousingTldManager>());

                services.AddSingleton<ItemIdManager>();
                services.AddSingleton<IItemIdManager>(sp => sp.GetRequiredService<ItemIdManager>());

                services.AddSingleton<MailIdManager>();
                services.AddSingleton<IMailIdManager>(sp => sp.GetRequiredService<MailIdManager>());

                services.AddSingleton<MateIdManager>();
                services.AddSingleton<IMateIdManager>(sp => sp.GetRequiredService<MateIdManager>());

                services.AddSingleton<MusicIdManager>();
                services.AddSingleton<IMusicIdManager>(sp => sp.GetRequiredService<MusicIdManager>());

                services.AddSingleton<ObjectIdManager>();
                services.AddSingleton<IObjectIdManager>(sp => sp.GetRequiredService<ObjectIdManager>());

                services.AddSingleton<PrivateBookIdManager>();
                services.AddSingleton<IPrivateBookIdManager>(sp => sp.GetRequiredService<PrivateBookIdManager>());

                services.AddSingleton<QuestIdManager>();
                services.AddSingleton<IQuestIdManager>(sp => sp.GetRequiredService<QuestIdManager>());

                services.AddSingleton<ShipyardIdManager>();
                services.AddSingleton<IShipyardIdManager>(sp => sp.GetRequiredService<ShipyardIdManager>());

                services.AddSingleton<TaskIdManager>();
                services.AddSingleton<ITaskIdManager>(sp => sp.GetRequiredService<TaskIdManager>());

                services.AddSingleton<TeamIdManager>();
                services.AddSingleton<ITeamIdManager>(sp => sp.GetRequiredService<TeamIdManager>());

                services.AddSingleton<TlIdManager>();
                services.AddSingleton<ITlIdManager>(sp => sp.GetRequiredService<TlIdManager>());

                services.AddSingleton<TradeIdManager>();
                services.AddSingleton<ITradeIdManager>(sp => sp.GetRequiredService<TradeIdManager>());

                services.AddSingleton<UccIdManager>();
                services.AddSingleton<IUccIdManager>(sp => sp.GetRequiredService<UccIdManager>());

                services.AddSingleton<VisitedSubZoneIdManager>();
                services.AddSingleton<IVisitedSubZoneIdManager>(sp => sp.GetRequiredService<VisitedSubZoneIdManager>());

                services.AddSingleton<WorldIdManager>();
                services.AddSingleton<IWorldIdManager>(sp => sp.GetRequiredService<WorldIdManager>());

                // -- Lazy<T> wrappers for circular-dep break patterns --
                // MS DI does NOT auto-wrap registered types in Lazy<T>; they must be registered explicitly.
                // WorldManager takes these three as Lazy<T> to break the circular dependency.
                services.AddSingleton(sp => new Lazy<IZoneManager>(sp.GetRequiredService<IZoneManager>));
                services.AddSingleton(sp => new Lazy<IIndunManager>(sp.GetRequiredService<IIndunManager>));
                services.AddSingleton(sp => new Lazy<IFamilyManager>(sp.GetRequiredService<IFamilyManager>));
                // NameManager ↔ CharacterManager circular dep: NameManager takes Lazy<ICharacterManager>.
                services.AddSingleton(sp => new Lazy<ICharacterManager>(sp.GetRequiredService<ICharacterManager>));
                // HousingManager ↔ MailManager circular dep: MailManager takes Lazy<IHousingManager>.
                services.AddSingleton(sp => new Lazy<IHousingManager>(sp.GetRequiredService<IHousingManager>));

                // -- Orchestrator --
                // Expose the ServiceCollection itself so ManagerOrchestrator can inspect registrations.
                services.AddSingleton(_ => services);
                services.AddSingleton<ManagerOrchestrator>();
            });

        try
        {
            await builder.RunConsoleAsync();
        }
        catch (OperationCanceledException ocex)
        {
            Logger.Fatal(ocex.Message);
        }
        return 0;
    }
    
    /// <summary>
    /// Tries to return a more human-readable OS name
    /// </summary>
    /// <returns></returns>
    private static string GetOsName()
    {
        try
        {
            // Note: This NuGet package can throw a exception in some cases, so we try to catch it
            return OSVersion.GetOperatingSystem().ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static void Initialization()
    {
        _thread.Name = "AA.Game Base Thread";
        _startTime = DateTime.UtcNow;
        Logger.Info($"{Name} version {Version}");
        Logger.Info($"Running as {(Environment.Is64BitProcess ? "64" : "32")}-bits on {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bits {GetOsName()} ({Environment.OSVersion})");
        if (!Environment.Is64BitProcess)
        {
            Logger.Warn($"Running in 32-bits mode is not recommended to do memory constraints");
        }
    }

    public static void ReloadConfiguration()
    {
        var configurationRoot = BuildConfiguration(_launchArgs);
        configurationRoot.Bind(AppConfiguration.Instance);
    }

    private static IConfigurationRoot BuildConfiguration(string[] args)
    {
        // Load NLog configuration
        LogManager.ThrowConfigExceptions = false;
        LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));

        // Load Game server configuration
        List<string> configFiles =
        [
            // Add the old main Config.json file
            Path.Combine(FileManager.AppPath, "Config.json"),
            // Get files inside the Configurations folder
            ..Directory.GetFiles(Path.Combine(FileManager.AppPath, "Configurations"), "*.json",
                SearchOption.AllDirectories).Order(),
            // Override all other config with the local file if it exists
            Path.Combine(FileManager.AppPath, "Config.Local.json")
        ];

        var configurationBuilder = new ConfigurationBuilder();

        // Add config json files
        foreach (var file in configFiles)
        {
            Logger.Info($"Config: {file}");
            configurationBuilder.AddJsonFile(file, optional: true);
        }

        configurationBuilder.AddUserSecrets<GameService>();
        configurationBuilder.AddEnvironmentVariables();
        configurationBuilder.AddCommandLine(args);

        return configurationBuilder.Build();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exceptionStr = e.ExceptionObject.ToString();
        Logger.Fatal(exceptionStr);
    }
}
