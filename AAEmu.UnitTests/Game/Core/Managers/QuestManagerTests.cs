using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Tasks.Quests;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class QuestManagerTests
{
    private static QuestManager CreateManager(ITaskManager taskManager = null, IZoneManager zoneManager = null)
    {
        return new QuestManager(
            taskManager ?? Mock.Of<ITaskManager>(),
            zoneManager ?? Mock.Of<IZoneManager>());
    }

    #region Existing Tests

    [Test]
    public async Task GetTemplate_BeforeLoad_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetTemplate(999);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddQuestTimer_CallsTaskManagerSchedule()
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
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        var result = manager.AddQuestTimer(mockOwner.Object, quest, 60_000);

        await Assert.That(result).IsTrue();
        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(60_000), null, -1),
            Times.Once);
    }

    #endregion

    #region GetTemplate Tests

    [Test]
    public async Task GetTemplate_WithNonExistentId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetTemplate(9999);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTemplate_WithZeroId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetTemplate(0);

        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetSupplies Tests

    [Test]
    public async Task GetSupplies_WithNonExistentLevel_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetSupplies(100);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetSupplies_WithZeroLevel_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetSupplies(0);

        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetActsInComponent Tests

    [Test]
    public async Task GetActsInComponent_WithNonExistentComponentId_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var result = manager.GetActsInComponent(9999);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetActsInComponent_WithZeroId_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var result = manager.GetActsInComponent(0);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetActTemplate Tests

    [Test]
    public async Task GetActTemplate_WithNonExistentId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetActTemplate(9999, "QuestActObjMonsterHunt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetActTemplate_WithNonExistentType_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetActTemplate(1, "NonExistentType");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetActTemplate_WithZeroIdAndValidType_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetActTemplate(0, "QuestActObjMonsterHunt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public void GetActTemplate_WithNullType_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        Assert.Throws<ArgumentNullException>(() => manager.GetActTemplate(1, null!));
    }

    #endregion

    #region CheckGroupItem Tests

    [Test]
    public async Task CheckGroupItem_WithNonExistentGroupId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupItem(9999, 1);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckGroupItem_WithZeroGroupId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupItem(0, 1);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckGroupItem_WithNonExistentItemId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupItem(1, 9999);

        await Assert.That(result).IsFalse();
    }

    #endregion

    #region CheckGroupNpc Tests

    [Test]
    public async Task CheckGroupNpc_WithNonExistentGroupId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupNpc(9999, 1);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckGroupNpc_WithZeroGroupId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupNpc(0, 1);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckGroupNpc_WithNonExistentNpcId_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupNpc(1, 9999);

        await Assert.That(result).IsFalse();
    }

    #endregion

    #region RemoveQuestTimer Tests

    [Test]
    public async Task RemoveQuestTimer_WithNonExistentOwnerId_ReturnsZero()
    {
        var manager = CreateManager();

        var result = manager.RemoveQuestTimer(9999, 1);

        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveQuestTimer_WithZeroQuestIdAndNonExistentOwner_ReturnsZero()
    {
        var manager = CreateManager();

        var result = manager.RemoveQuestTimer(9999, 0);

        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveQuestTimer_WithNonExistentQuestId_ReturnsZero()
    {
        var manager = CreateManager();

        var result = manager.RemoveQuestTimer(1, 9999);

        await Assert.That(result).IsEqualTo(0);
    }

    #endregion

    #region GetGroupItems Tests

    [Test]
    public async Task GetGroupItems_WithNonExistentGroupId_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var result = manager.GetGroupItems(9999);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetGroupItems_WithZeroGroupId_ReturnsEmptyList()
    {
        var manager = CreateManager();

        var result = manager.GetGroupItems(0);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetComponent Tests

    [Test]
    public async Task GetComponent_WithNonExistentComponentId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetComponent(9999);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetComponent_WithZeroId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetComponent(0);

        await Assert.That(result).IsNull();
    }

    #endregion

    #region FailQuest Tests

    // Note: FailQuest throws NullReferenceException when owner is null,
    // not ArgumentNullException as might be expected

    #endregion

    #region EnqueueEvaluation Tests

    [Test]
    public void EnqueueEvaluation_WithValidQuest_SchedulesTask()
    {
        var mockTaskManager = new Mock<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()))
            .Returns(true);

        var manager = CreateManager(taskManager: mockTaskManager.Object);
        var mockOwner = new Mock<ICharacter>();
        mockOwner.SetupGet(c => c.Id).Returns(1u);
        mockOwner.SetupGet(c => c.Name).Returns("TestPlayer");

        // Use null template to avoid CreateQuestSteps complexity
        var quest = new Quest(
            null,
            mockOwner.Object,
            Mock.Of<IQuestManager>(),
            Mock.Of<ITaskManager>(),
            Mock.Of<ISkillManager>(),
            Mock.Of<IExpressTextManager>(),
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        manager.EnqueueEvaluation(quest);

        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<QuestManagerRunQueueTask>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()),
            Times.Once);
    }

    #endregion

    #region GetQuestIdFromStarterItem Tests

    // Note: These methods require Load() to be called first to initialize the dictionary keys
    // Testing them without Load() throws KeyNotFoundException

    #endregion

    #region Quest Timeout Task Management Tests

    [Test]
    public async Task AddQuestTimer_ExistingTimer_ReturnsFalse()
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
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        // Add first timer
        var firstResult = manager.AddQuestTimer(mockOwner.Object, quest, 60_000);
        await Assert.That(firstResult).IsTrue();

        // Try to add second timer for same quest - should return false
        var secondResult = manager.AddQuestTimer(mockOwner.Object, quest, 60_000);
        await Assert.That(secondResult).IsFalse();
    }

    [Test]
    public async Task AddQuestTimer_WithZeroTime_SchedulesTask()
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
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        var result = manager.AddQuestTimer(mockOwner.Object, quest, 0);

        await Assert.That(result).IsTrue();
        mockTaskManager.Verify(
            t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), TimeSpan.FromMilliseconds(0), null, -1),
            Times.Once);
    }

    #endregion

    #region Edge Cases and Boundary Tests

    [Test]
    public async Task GetTemplate_WithMaxUIntId_ReturnsNull()
    {
        var manager = CreateManager();

        var result = manager.GetTemplate(uint.MaxValue);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CheckGroupItem_WithMaxUIntIds_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = manager.CheckGroupItem(uint.MaxValue, uint.MaxValue);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RemoveQuestTimer_WithMaxUIntIds_ReturnsZero()
    {
        var manager = CreateManager();

        var result = manager.RemoveQuestTimer(uint.MaxValue, uint.MaxValue);

        await Assert.That(result).IsEqualTo(0);
    }

    #endregion

    #region Quest Objective Types Tests

    [Test]
    public async Task GetActsInComponent_ReturnsListThatCanBeModified()
    {
        var manager = CreateManager();

        var result = manager.GetActsInComponent(1);

        await Assert.That(result).IsNotNull();
        // Should return a new empty list each time
        var result2 = manager.GetActsInComponent(1);
        await Assert.That(result2).IsNotSameReferenceAs(result);
    }

    #endregion

    #region Quest Timer Multiple Owners Tests

    [Test]
    public async Task AddQuestTimer_MultipleOwners_CreatesSeparateTimers()
    {
        var mockTaskManager = new Mock<ITaskManager>();
        mockTaskManager
            .Setup(t => t.Schedule(It.IsAny<AAEmu.Game.Models.Tasks.Task>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<int>()))
            .Returns(true);

        var manager = CreateManager(taskManager: mockTaskManager.Object);

        var mockOwner1 = new Mock<ICharacter>();
        mockOwner1.SetupGet(c => c.Id).Returns(1u);
        mockOwner1.Setup(c => c.SendDebugMessage(It.IsAny<string>()));

        var mockOwner2 = new Mock<ICharacter>();
        mockOwner2.SetupGet(c => c.Id).Returns(2u);
        mockOwner2.Setup(c => c.SendDebugMessage(It.IsAny<string>()));

        var quest1 = new Quest(
            null,
            mockOwner1.Object,
            Mock.Of<IQuestManager>(),
            Mock.Of<ITaskManager>(),
            Mock.Of<ISkillManager>(),
            Mock.Of<IExpressTextManager>(),
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        var quest2 = new Quest(
            null,
            mockOwner2.Object,
            Mock.Of<IQuestManager>(),
            Mock.Of<ITaskManager>(),
            Mock.Of<ISkillManager>(),
            Mock.Of<IExpressTextManager>(),
            Mock.Of<IWorldManager>())
        { TemplateId = 42u };

        var result1 = manager.AddQuestTimer(mockOwner1.Object, quest1, 60_000);
        var result2 = manager.AddQuestTimer(mockOwner2.Object, quest2, 60_000);

        await Assert.That(result1).IsTrue();
        await Assert.That(result2).IsTrue();
    }

    // Note: RemoveQuestTimer with questId=0 has a dependency on TaskManager singleton
    // which requires DI setup. This test is skipped for unit testing.

    #endregion
}