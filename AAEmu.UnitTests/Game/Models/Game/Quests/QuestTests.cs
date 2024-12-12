using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using Moq;
using Xunit;

#pragma warning disable IDE0051

namespace AAEmu.UnitTests.Game.Models.Game.Quests;
// TODO: Re-enable the quest related test
// ReSharper disable UnusedMember.Local

public class QuestTests
{
    // [Fact]
    private void Start_WhenQuestStepIsNoneAndComponentIsEmpty_ShouldDoNothing()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _, out _, out _);
        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns([]);

        // Act
        var result = quest.StartQuest();

        // Assert
        Assert.False(result);
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    // [Fact]
    private void Start_WhenQuestActsIsEmpty_ShouldDoNothing()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
        var expectedIds = new List<uint>();

        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>()))
            .Returns<QuestComponentKind>(kind => [new QuestComponentTemplate(null) { KindId = kind }])
            .Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));

        // Act
        var result = quest.StartQuest();

        // Assert
        Assert.False(result);
        foreach (var exceptedId in expectedIds)
        {
            mockQuestManager.Verify(qm => qm.GetActsInComponent(It.IsIn(exceptedId)), Times.Once);
        }
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    // [Fact]
    private void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetNotMatch_ShouldAbort()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
        var expectedIds = new List<uint>();

        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => [
            new QuestComponentTemplate(null) { Id = (uint)kind }
        ]).Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));

        var mockQuestAct = new Mock<QuestActTemplate>();
        mockQuestAct.Setup(qa => qa.RunAct(It.IsAny<Quest>(), It.IsAny<QuestAct>(), It.IsAny<int>())).Returns(false);
        mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");

        mockQuestManager.Setup(qm => qm.GetActsInComponent(It.IsAny<uint>())).Returns(new[] {
            mockQuestAct.Object
        }.ToList());

        // Act
        var result = quest.StartQuest();

        // Assert
        Assert.False(result);
        foreach (var exceptedId in expectedIds)
        {
            mockQuestManager.Verify(qm => qm.GetActsInComponent(It.IsIn(exceptedId)), Times.Once);
        }
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
    }

    // [Fact]
    private void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetMatch_ButComponentSkillIdIsZero_ShouldNotOwnerUseSkill()
    {
        // Arrange
        var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
        var expectedIds = new List<uint>();
        mockQuestTemplate.SetupGet(qt => qt.Components).Returns(new Dictionary<uint, QuestComponentTemplate>()
        {
            { 1, new QuestComponentTemplate(null) { Id = 1, KindId = QuestComponentKind.Drop } },
            { 2, new QuestComponentTemplate(null) { Id = 2, KindId = QuestComponentKind.Drop } }
        });
        mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => [
            new QuestComponentTemplate(null) { Id = (uint)kind }
        ]);

        var mockQuestAct = new Mock<QuestActTemplate>();
        mockQuestAct.Setup(qa => qa.RunAct(It.IsAny<Quest>(), It.IsAny<QuestAct>(), It.IsAny<int>())).Returns(true);
        mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");
        mockQuestManager.Setup(qm => qm.GetActsInComponent(It.IsAny<uint>())).Returns(new[] {
            mockQuestAct.Object
        }.ToList);

        // Act
        var result = quest.StartQuest();

        // Assert
        Assert.True(result);
        mockOwner.Verify(o => o.UseSkill(It.IsAny<uint>(), It.IsAny<ICharacter>()), Times.Never);
        mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
    }

    private static Quest SetupQuest(
        out Mock<ICharacter> mockCharacter,
        out Mock<IQuestTemplate> mockQuestTemplate,
        out Mock<IQuestManager> mockQuestManager,
        out Mock<ISphereQuestManager> mockSphereQuestManager,
        out Mock<TaskManager> mockTaskManager,
        out Mock<ISkillManager> mockSkillManager,
        out Mock<IExpressTextManager> mockExpressTextManager,
        out Mock<IWorldManager> mockWorldManager)
    {
        mockCharacter = new Mock<ICharacter>();
        mockQuestManager = new Mock<IQuestManager>();
        mockQuestTemplate = new Mock<IQuestTemplate>();
        mockQuestTemplate.SetupGet(x => x.Components).Returns(new Dictionary<uint, QuestComponentTemplate>());
        mockSphereQuestManager = new Mock<ISphereQuestManager>();
        mockExpressTextManager = new Mock<IExpressTextManager>();
        mockSkillManager = new Mock<ISkillManager>();
        mockTaskManager = new Mock<TaskManager>();
        mockWorldManager = new Mock<IWorldManager>();

        var quest = new Quest(
            mockQuestTemplate.Object,
            mockCharacter.Object,
            mockQuestManager.Object,
            mockSphereQuestManager.Object,
            mockTaskManager.Object,
            mockSkillManager.Object,
            mockExpressTextManager.Object,
            mockWorldManager.Object);

        quest.Owner = mockCharacter.Object;
        quest.Template = mockQuestTemplate.Object;
        return quest;
    }
}
