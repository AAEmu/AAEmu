using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Utils.DB;
using Moq;
using Xunit;

namespace AAEmu.Tests.Models.Game.Quests
{
    public class QuestTests
    {
        private QuestManager _questManager = new QuestManager();
        private QuestManagerFake _questManagerFake = new QuestManagerFake();
        public QuestTests()
        {
            //Loads all quests from DB
            QuestManager.Instance.Load();
            _questManagerFake.Load();
        }
        
        [Theory]
        [InlineData(871)]
        public void Start_WhenQuestStart_AllActsAreQuestActConAcceptNpc_And_TargetNpcIsNotValid_ShouldEndQuick(uint questId)
        {
            // Arrange
            var questsWithAcceptNpcs = _questManagerFake.GetQuestIdsWithActContainingActDetailType("QuestActConAcceptNpc");



            var quest = SetupQuest(questId, QuestManager.Instance, out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _);
            
            // Act
            var result = quest.Start();

            // Assert
            Assert.False(result);
            mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
        }

        private Quest SetupQuest(
            uint questId,
            IQuestManager questManager,
            out Mock<ICharacter> mockCharacter,
            out Mock<IQuestTemplate> mockQuestTemplate,
            out Mock<ISphereQuestManager> mockSphereQuestManager,
            out Mock<ITaskManager> mockTaskManager,
            out Mock<ISkillManager> mockSkillManager,
            out Mock<IExpressTextManager> mockExpressTextManager)
        {
            mockCharacter = new Mock<ICharacter>();
            mockQuestTemplate = new Mock<IQuestTemplate>();
            mockSphereQuestManager = new Mock<ISphereQuestManager>();
            mockExpressTextManager = new Mock<IExpressTextManager>();
            mockSkillManager = new Mock<ISkillManager>();
            mockTaskManager = new Mock<ITaskManager>();

            var quest = new Quest(
                questManager.GetTemplate(questId),
                questManager,
                mockSphereQuestManager.Object,
                mockTaskManager.Object,
                mockSkillManager.Object,
                mockExpressTextManager.Object);

            quest.Owner = mockCharacter.Object;
            return quest;
        }
    }

    public class QuestManagerFake : QuestManager {
        public IEnumerable<QuestTemplate> Templates => _templates.Values;

        public IEnumerable<uint> GetQuestIdsWithActContainingActDetailType(string detailType)
        {
            List<uint> questIds = new();
            
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"select 
                                                qc.quest_context_id 
                                            from 
                                                quest_components qc
                                                inner join quest_acts qa 
                                                    on qc.id = qa.quest_component_id
                                            where 
                                                qa.act_detail_type = '{detailType}'
                                            order by 
                                                quest_context_id";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            questIds.Add(reader.GetUInt32("quest_context_id"));
                        }
                    }
                }
            }

            return questIds;
        }
    }
}
