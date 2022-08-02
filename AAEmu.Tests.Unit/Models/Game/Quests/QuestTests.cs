using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using Moq;
using Xunit;

namespace AAEmu.Tests.Unit.Models.Game.Quests
{
    public class QuestTests
    {
        [Fact]
        public void Start_WhenQuestStepIsNoneAndComponentIsEmpty_ShouldDoNothing()
        {
            // Arrange
            var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _, out _, out _);
            mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns(Array.Empty<QuestComponent>());
            
            // Act
            var result = quest.Start();

            // Assert
            Assert.False(result);
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
        }


        [Fact]
        public void Start_WhenQuestActsIsEmpty_ShouldDoNothing()
        {
            // Arrange
            var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
            var expectedIds = new List<uint>();

            mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => new[] {
                new QuestComponent() { Id = (uint)kind }
            }).Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));
            
           
            // Act
            var result = quest.Start();

            // Assert
            Assert.False(result);
            foreach (var exceptedId in expectedIds)
            {
                mockQuestManager.Verify(qm => qm.GetActs(It.IsIn(exceptedId)), Times.Once);
            }
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
        }

        [Fact]
        public void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetNotMatch_ShouldAbort()
        {
            // Arrange
            var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
            var expectedIds = new List<uint>();

            mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => new[] {
                new QuestComponent() { Id = (uint)kind }
            }).Callback<QuestComponentKind>(d => expectedIds.Add((uint)d));

            var mockQuestAct = new Mock<IQuestAct>();
            mockQuestAct.Setup(qa => qa.Use(It.IsAny<ICharacter>(), It.IsAny<Quest>(), It.IsAny<int>())).Returns(false);
            mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");
            
            mockQuestManager.Setup(qm => qm.GetActs(It.IsAny<uint>())).Returns(new[] {
                mockQuestAct.Object, mockQuestAct.Object
            });

            // Act
            var result = quest.Start();

            // Assert
            Assert.False(result);
            foreach (var exceptedId in expectedIds)
            {
                mockQuestManager.Verify(qm => qm.GetActs(It.IsIn(exceptedId)), Times.Once);
            }
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
        }

        [Fact]
        public void Start_WhenComponentActsAreAllQuestActConAcceptNpc_AndTargetMatch_ButComponentSkillIdIsZero_ShouldNotOwnerUseSkill()
        {
            // Arrange
            var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out var mockQuestManager, out _, out _, out _, out _, out _);
            var expectedIds = new List<uint>();
            mockQuestTemplate.SetupGet(qt => qt.Components).Returns(new Dictionary<uint, QuestComponent>()
            {
                { 1, new QuestComponent() { Id = 1, KindId = QuestComponentKind.Drop } },
                { 2, new QuestComponent() { Id = 2, KindId = QuestComponentKind.Drop } }
            });
            mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns<QuestComponentKind>(kind => new[] {
                new QuestComponent() { Id = (uint)kind }
            });

            var mockQuestAct = new Mock<IQuestAct>();
            mockQuestAct.Setup(qa => qa.Use(It.IsAny<ICharacter>(), It.IsAny<Quest>(), It.IsAny<int>())).Returns(true);
            mockQuestAct.SetupGet(qa => qa.DetailType).Returns("QuestActConAcceptNpc");
            mockQuestManager.Setup(qm => qm.GetActs(It.IsAny<uint>())).Returns(new[] {
                mockQuestAct.Object, mockQuestAct.Object
            });

            // Act
            var result = quest.Start();

            // Assert
            Assert.True(result);
            mockOwner.Verify(o => o.UseSkill(It.IsAny<uint>(), It.IsAny<ICharacter>()), Times.Never);
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
        }
        
        private Quest SetupQuest(
            out Mock<ICharacter> mockCharacter,
            out Mock<IQuestTemplate> mockQuestTemplate,
            out Mock<IQuestManager> mockQuestManager, 
            out Mock<ISphereQuestManager> mockSphereQuestManager,
            out Mock<ITaskManager> mockTaskManager,
            out Mock<ISkillManager> mockSkillManager,
            out Mock<IExpressTextManager> mockExpressTextManager,
            out Mock<IWorldManager> mockWorldManager)
        {
            mockCharacter = new Mock<ICharacter>();
            mockQuestManager = new Mock<IQuestManager>();
            mockQuestTemplate = new Mock<IQuestTemplate>();
            mockSphereQuestManager = new Mock<ISphereQuestManager>();
            mockExpressTextManager = new Mock<IExpressTextManager>();
            mockSkillManager = new Mock<ISkillManager>();
            mockTaskManager = new Mock<ITaskManager>();
            mockWorldManager = new Mock<IWorldManager>();
            
            var quest = new Quest(
                mockQuestTemplate.Object,
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
}
