using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using Moq;
using Xunit;

namespace AAEmu.Tests.Models.Game.Quests
{
    public class QuestTests
    {
        private QuestManager _questManager = new QuestManager();
        public QuestTests()
        {
            //Loads all quests from DB
            QuestManager.Instance.Load();
        }
        
        [Fact]
        public void Start_WhenQuestStart_AllActsAreQuestActConAcceptNpc_And_TargetNpcIsNotValid_ShouldEndQuick()
        {
            // Arrange
            var questIds = GetAllQuests_Where_ComponentKindStart_HasAllActsAs_QuestActConAcceptNpc();

            foreach(var questId in questIds)
            {
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _);

                // Act
                var result = quest.Start();
                
                // Assert
                Assert.False(result);
                mockOwner.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
                mockOwner.Verify(o => o.UseSkill(It.IsAny<uint>(), It.IsAny<IUnit>()), Times.Never);
            }
        }

        [Fact]
        public void Test_SpecificQuest()
        {
            // Arrange
            var questIds = GetQuestIdsWithActContainingActDetailType("QuestActConAcceptDoodad");
            foreach (var questId in questIds)
            {
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockOwner, out var mockQuestTemplate, out _, out _, out _, out _);

                // Act
                var result = quest.Start();

                // Assert
                var questTemplate = QuestManager.Instance.GetTemplate(questId);
                var startComponents = questTemplate.GetFirstComponent(QuestComponentKind.Start);
                var hasSupplyComponent = questTemplate.GetComponents(QuestComponentKind.Supply).Length > 0;
                if (!hasSupplyComponent)
                {
                    Assert.Equal(QuestStatus.Progress, quest.Status);
                }
                Assert.False(result);
            }
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
        
        public IEnumerable<uint> GetAllQuests_Where_ComponentKindStart_HasAllActsAs_QuestActConAcceptNpc()
        {
            List<uint> questIds = new();

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"select 
                                                qc.quest_context_id, qa.act_detail_type, qaNot.act_detail_type
                                            from 
	                                            quest_contexts qcx
	                                            inner join quest_components qc
		                                            on qcx.id = qc.quest_context_id 
                                                inner join quest_acts qa 
                                                    on qc.id = qa.quest_component_id and qa.act_detail_type = 'QuestActConAcceptNpc'
                                                left join quest_acts qaNot
    	                                            on qc.id = qaNot.quest_component_id and qaNot.act_detail_type <> 'QuestActConAcceptNpc'
                                            where 
	                                            qc.component_kind_id = 2 and qaNot.act_detail_type is null
                                            group by
	                                            qc.quest_context_id, qa.act_detail_type
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
