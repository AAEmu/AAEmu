using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AAEmu.IntegrationTests.Core.Manager;

// Minimal test implementation to satisfy DI for ZoneManager construction
public class TestWorldManager : IWorldManager
{
    public WorldInstance MainWorld { get; set; }

    public void CreateStaticInstances() { }

    public WorldInstance CreateWorldInstance(WorldTemplate worldTemplate, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0, Character notifyPlayer = null) => null;

    public WorldTemplate CreateWorldTemplate(string worldName) => null;

    public Character GetCharacterByObjId(uint id) => null;
    public Character GetCharacterById(uint id) => null;
    public Character GetCharacter(string name) => null;
    public List<Character> GetAllCharacters() => [];

    public uint GetZoneId(WorldTemplate worldTemplate, float x, float y) => 0;
    public WorldTemplate GetWorldTemplateByName(string worldName) => null;
    public WorldTemplate GetWorldTemplateByZoneKey(uint zoneKey) => null;
    public WorldInstance[] GetWorlds() => [];
    public WorldInstance GetWorld(uint worldInstanceId) => null;
    public List<uint> GetZoneKeysByWorldId(uint worldId) => [];
    public void BroadcastPacketToServer(GamePacket packet) { }
    public Character GetTargetOrSelf(Character character, string targetName, out int firstNonNameArgument) { firstNonNameArgument = 0; return character; }
    public bool TryRemoveCharacter(uint playerObjId) => false;
    public void Initialize() { }
    public WorldTemplate[] GetAllWorldTemplates() => [];
    public void Load() { }
}

public class QuestManagerTests
{
    private static bool _managersLoaded = false;

    private static void LoadManagers()
    {
        if (_managersLoaded)
            return;

        // Locate Config.json in the test project directory
        var testProjectDir = Path.GetDirectoryName(typeof(QuestManagerTests).Assembly.Location);
        var mainConfig = Path.Combine(testProjectDir, "Config.json");

        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile(mainConfig);
        configurationBuilder.AddUserSecrets<QuestManager>();
        var configurationBuilderResult = configurationBuilder.Build();
        configurationBuilderResult.Bind(AppConfiguration.Instance);

        MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);

        // Configure a minimal DI container so singletons with constructor dependencies can be resolved
        var services = new ServiceCollection();
        services.AddSingleton<ITickManager, TickManager>();
        services.AddSingleton<TaskManager>();
        services.AddSingleton<ITaskManager>(sp => sp.GetRequiredService<TaskManager>());

        // Register a lightweight WorldManager implementation and the ZoneManager so ZoneManager can be resolved from DI
        services.AddSingleton<TestWorldManager>();
        services.AddSingleton<IWorldManager>(sp => sp.GetRequiredService<TestWorldManager>());
        services.AddSingleton<ZoneManager>();
        services.AddSingleton<IZoneManager>(sp => sp.GetRequiredService<ZoneManager>());
        services.AddSingleton<QuestManager>();
        services.AddSingleton<IQuestManager>(sp => sp.GetRequiredService<QuestManager>());

        AAEmu.Commons.Utils.SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        // Loads all quests from DB
        TaskIdManager.Instance.Initialize();
        TaskManager.Instance.Initialize();
        // ZoneManager.Instance.Load(); // Skipped in tests to avoid requiring WorldManager DI
        QuestManager.Instance.Load();

        _managersLoaded = true;
    }

    public QuestManagerTests()
    {
        LoadManagers();
    }

    [Fact]
    public Task GetQuestIdFromStarterItem_ShouldReturnSameResultAsOriginal()
    {
        Dictionary<uint, uint> expectedResults = [];
        var query = @"
        SELECT qc2.id as questId, qacai.item_id as itemId
        FROM 
            quest_act_con_accept_items qacai
                INNER JOIN quest_acts qa 
                    ON qacai.id = qa.act_detail_id and qa.act_detail_type = 'QuestActConAcceptItem'
                INNER JOIN quest_components qc 
                    ON qc.id = qa.quest_component_id 
                INNER JOIN quest_contexts qc2 
                    ON qc2.id = qc.quest_context_id ";

        using (var connection = SQLite.CreateConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var questId = reader.GetUInt32("questId");
                    var itemId = reader.GetUInt32("itemId");

                    expectedResults.Add(itemId, questId);
                }
            }
        }

        foreach (var (itemId, questId) in expectedResults)
        {
            var resultOld = QuestManager.Instance.GetQuestIdFromStarterItem(itemId);
            Assert.Equal(questId, resultOld);

            var resultNew = QuestManager.Instance.GetQuestIdFromStarterItemNew(itemId);
            Assert.Equal(questId, resultNew);
        }

        return Task.CompletedTask;
    }
}
