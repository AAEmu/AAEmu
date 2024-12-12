using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using Microsoft.Extensions.Configuration;
using System.IO;
using AAEmu.Game.Models;
using Xunit;
using System.Threading.Tasks;
using AAEmu.Game.Utils.DB;
using System.Collections.Generic;

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

        // Loads all quests from DB
        TaskIdManager.Instance.Initialize();
        TaskManager.Instance.Initialize();
        ZoneManager.Instance.Load();
        QuestManager.Instance.Load();
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
