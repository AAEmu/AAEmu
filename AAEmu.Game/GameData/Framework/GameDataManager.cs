using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.GameData.Framework;

/// <summary>
/// Orchestrates all <see cref="IGameDataLoader"/> implementations.
/// The constructor deps (Localization, Taxations, Item, Quest, Zone) are declared here
/// so the <see cref="ManagerOrchestrator"/> knows those managers must complete their
/// <c>Load()</c> before <c>GameDataManager.Load()</c> runs — their data is accessed by
/// individual game-data loaders (HousingGameData, SphereGameData, etc.) via static Instance.
/// </summary>
public class GameDataManager(
    ILocalizationManager localizationManager,
    ITaxationsManager taxationsManager,
    IItemManager itemManager,
    IQuestManager questManager,
    IZoneManager zoneManager
) : Singleton<GameDataManager>, IGameDataManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<IGameDataLoader> _loaders = [];
    private bool _loadedGameData = false;
    private bool _postLoadedGameData = false;

    // Stored so DI keeps references alive and to suppress CS9113 (unread primary ctor param).
    private readonly ILocalizationManager _localizationManager = localizationManager;
    private readonly ITaxationsManager _taxationsManager = taxationsManager;
    private readonly IItemManager _itemManager = itemManager;
    private readonly IQuestManager _questManager = questManager;
    private readonly IZoneManager _zoneManager = zoneManager;

    /// <inheritdoc cref="ILoadable.Load"/>
    public void Load() => LoadGameData();

    public void LoadGameData()
    {
        if (_loadedGameData)
            return;

        Logger.Info("Loading game data");
        CreateLoaders();
        // 10.0.2.13: loaders are intentionally NOT wrapped in a swallow-and-skip try/catch. A loader failure now
        // propagates (startup fails with a full stack trace) so DB-migration mismatches surface immediately
        // instead of being silently skipped.
        using (var connection = SQLite.CreateConnection())
        {
            foreach (var loader in _loaders)
            {
                Logger.Info("Loading {0}", loader.GetType().Name);
                loader.Load(connection);
                Logger.Info("Loaded {0}", loader.GetType().Name);
            }
        }

        Logger.Info("Game data loaded");

        _loadedGameData = true;
    }

    public void PostLoadGameData()
    {
        if (_postLoadedGameData)
            return;

        Logger.Info("Post loading game data");
        foreach (var loader in _loaders)
        {
            Logger.Info("Post loading {0}", loader.GetType().Name);
            loader.PostLoad();
            Logger.Info("Post loaded {0}", loader.GetType().Name);
        }
        Logger.Info("Game data post loaded");

        _postLoadedGameData = true;
    }

    private void CreateLoaders()
    {
        foreach (var type in Assembly.GetAssembly(typeof(GameDataManager)).GetTypes())
        {
            if (type.GetCustomAttributes(typeof(GameDataAttribute), true).Length <= 0)
                continue;

            if (!type.GetInterfaces().Contains(typeof(IGameDataLoader)))
            {
                Logger.Error("[GameData] {0} does not inherit IGameDataLoader", type.Name);
                continue;
            }

            var e = type.BaseType?.GetProperty("Instance")?.GetValue(null);
            Register((IGameDataLoader)e);
        }
    }

    private void Register(IGameDataLoader dataLoader)
    {
        _loaders.Add(dataLoader);
    }
}
