using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.Configuration;
using AAEmu.Game.Models;
using Xunit;
using AAEmu.Game.Utils.DB;
using Moq;

namespace AAEmu.IntegrationTests.Core.Manager;

public class QuestManagerTests
{
    private static bool _managersLoaded = false;

    private static void LoadManagers()
    {
        if (_managersLoaded)
            return;

        var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddJsonFile(mainConfig);
        configurationBuilder.AddUserSecrets<QuestManager>();
        var configurationBuilderResult = configurationBuilder.Build();
        configurationBuilderResult.Bind(AppConfiguration.Instance);

        MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);

        // Setup TaskManager with mock ITickManager for tests
        var mockTickManager = new Mock<ITickManager>();
        mockTickManager.SetupGet(t => t.OnTick).Returns(new TickManager.TickEventHandler());
        var taskManager = new TaskManager(mockTickManager.Object);
        taskManager.Initialize();

        // Setup ZoneManager with mock IWorldManager for tests
        var mockWorldManager = new Mock<IWorldManager>();
        var zoneManager = new ZoneManager(mockWorldManager.Object);
        zoneManager.Load();

        // Loads all quests from DB
        TaskIdManager.Instance.Initialize();
        QuestManager.Instance.Load();

        _managersLoaded = true;
    }

    public QuestManagerTests()
    {
        LoadManagers();
    }

    [Fact(Skip = "Requires full DI container setup with database dependencies")]
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
