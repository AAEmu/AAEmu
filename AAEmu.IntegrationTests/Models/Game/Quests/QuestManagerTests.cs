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

namespace AAEmu.IntegrationTests.Models.Game.Quests;

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
        var firstValue = QuestManager.Instance.GetQuestIdFromStarterItem(19576);

        return Task.CompletedTask;
    }
}
