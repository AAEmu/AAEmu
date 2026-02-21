using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class QuestManagerTests
{
    private static QuestManager CreateManager(ITaskManager taskManager = null, IZoneManager zoneManager = null)
    {
        return new QuestManager(
            taskManager ?? Mock.Of<ITaskManager>(),
            zoneManager ?? Mock.Of<IZoneManager>());
    }

    [Fact]
    public void GetTemplate_BeforeLoad_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetTemplate(999);

        Assert.Null(result);
    }

    [Fact]
    public void AddQuestTimer_CallsTaskManagerSchedule()
    {
        var mockTaskManager = new Mock<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()))
            .Returns(true);

        var manager = CreateManager(taskManager: mockTaskManager.Object);

        var mockOwner = new Mock<ICharacter>();
        mockOwner.SetupGet(c => c.Id).Returns(1u);
        mockOwner.Setup(c => c.SendDebugMessage(It.IsAny<string>()));

        var quest = new Quest(
            null,
            mockOwner.Object,
            Mock.Of<IQuestManager>(),
            Mock.Of<ITaskManager>(),
            Mock.Of<ISkillManager>(),
            Mock.Of<IExpressTextManager>(),
            Mock.Of<IWorldManager>()) { TemplateId = 42u };

        var result = manager.AddQuestTimer(mockOwner.Object, quest, 60_000);

        Assert.True(result);
        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(60_000), null, -1),
            Times.Once);
    }
}
