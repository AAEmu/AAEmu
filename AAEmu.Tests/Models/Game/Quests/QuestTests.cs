using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using Moq;
using Xunit;

namespace AAEmu.Tests.Models.Game.Quests
{
    public class QuestTests
    {
        [Fact]
        public void Start_WhenQuestComponentIsEmpty_ShouldDoNothing()
        {
            // Arrange
            var quest = SetupQuest(out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _, out _);
            mockQuestTemplate.Setup(qt => qt.GetComponents(It.IsAny<QuestComponentKind>())).Returns(new QuestComponent[] { });
            
            // Act
            var result = quest.Start();

            // Assert
            Assert.False(result);
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Once);
        }

        private Quest SetupQuest(
            out Mock<ICharacter> mockCharacter,
            out Mock<IQuestTemplate> mockQuestTemplate,
            out Mock<IQuestManager> mockQuestManager, 
            out Mock<ISphereQuestManager> mockSphereQuestManager,
            out Mock<ITaskManager> mockTaskManager,
            out Mock<ISkillManager> mockSkillManager,
            out Mock<IExpressTextManager> mockExpressTextManager)
        {
            mockCharacter = new Mock<ICharacter>();
            mockQuestManager = new Mock<IQuestManager>();
            mockQuestTemplate = new Mock<IQuestTemplate>();
            mockSphereQuestManager = new Mock<ISphereQuestManager>();
            mockExpressTextManager = new Mock<IExpressTextManager>();
            mockSkillManager = new Mock<ISkillManager>();
            mockTaskManager = new Mock<ITaskManager>();

            var quest = new Quest(
                mockQuestManager.Object,
                mockSphereQuestManager.Object,
                mockTaskManager.Object,
                mockSkillManager.Object,
                mockExpressTextManager.Object);

            quest.Owner = mockCharacter.Object;
            quest.Template = mockQuestTemplate.Object;
            return quest;
        }
    }
}
