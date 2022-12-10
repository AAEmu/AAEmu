using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace AAEmu.Tests.Integration.Models.Game.Quests
{
    public class QuestTests
    {
        private static bool _managersLoaded = false;
        
        private void LoadManagers()
        {
            if (_managersLoaded)
                return;

            var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
            var configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddJsonFile(mainConfig);
            var configurationBuilderResult = configurationBuilder.Build();
            configurationBuilderResult.Bind(AppConfiguration.Instance);

            // Loads all quests from DB
            QuestManager.Instance.Load();
            FormulaManager.Instance.Load();
            ItemManager.Instance.Load();
            PlotManager.Instance.Load();
            SkillManager.Instance.Load();

            ClientFileManager.Initialize();
            TlIdManager.Instance.Initialize();
            ObjectIdManager.Instance.Initialize();
            TaskIdManager.Instance.Initialize();
            TaskManager.Instance.Initialize();
            ContainerIdManager.Instance.Initialize();
            ItemIdManager.Instance.Initialize();
            ItemManager.Instance.LoadUserItems();
            WorldManager.Instance.Load();
            FactionManager.Instance.Load();
            ModelManager.Instance.Load();

            AIManager.Instance.Initialize();
            GameDataManager.Instance.LoadGameData();
            GameScheduleManager.Instance.Load();
            NpcManager.Instance.Load();
            DoodadManager.Instance.Load();
            TransferManager.Instance.Load();
            GimmickManager.Instance.Load();
            SpawnManager.Instance.Load();

            GameDataManager.Instance.PostLoadGameData();
            SpawnManager.Instance.SpawnAllNpcs(0);

            _managersLoaded = true;
        }
        public QuestTests()
        {
            LoadManagers();
        }

        [Fact]
        public void Start_WhenQuestStart_AllActsAreQuestActConAcceptNpc_And_TargetNpcIsNotValid_ShouldNotStartQuest()
        {
            // Arrange
            var questIds = GetAllQuests_Where_ComponentKindStart_HasAllActsAs_QuestActConAcceptNpc().ToArray();
            var count = 0;
            Random rnd = new Random();

            // Randomizing first 500 due to performance impact... 
            foreach (var questId in questIds.OrderBy(x => rnd.Next()).Take(500))
            {
                count++;
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, out _);
                

                // Act
                var result = quest.Start();
                
                // Assert 
                Assert.False(result); // Quest not started
                mockCharacter.Verify(o => o.SendPacket(It.IsAny<SCQuestContextStartedPacket>()), Times.Never);
                mockCharacter.Verify(o => o.UseSkill(It.IsAny<uint>(), It.IsAny<IUnit>()), Times.Never);

                Trace.WriteLine($"{questId} - {count}/{questIds.Length}");
                mockCharacter.Invocations.Clear();
                mockCharacter.Reset();
            }
        }

        [Fact]
        public void Start_DoodadQuestsSupplyingItem_WhenBagIsNotFull_ShouldAddToCharacterBag()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new (QuestComponentKind.Start,"QuestActConAcceptDoodad"),
                new (QuestComponentKind.Supply, "QuestActSupplyItem")
            );

            // Excluding sphere to avoid Update();
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);

            foreach (var questId in targetQuestIds)
            {
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, out _);
                SetupCharacter(mockCharacter);
                
                // Act
                var result = quest.Start();

                var supplyItemAct = quest.Template.GetFirstComponent(QuestComponentKind.Supply).ActTemplates.OfType<QuestActSupplyItem>().FirstOrDefault();
                if (supplyItemAct is not null)
                {
                    Assert.Equal(supplyItemAct.Count, mockCharacter.Object.Inventory.GetItemsCount(supplyItemAct.ItemId));
                }
                
                //Started successfuly
                Assert.True(result);
            }
        }

        [Fact]
        public void Start_DoodadQuestsSupplyingItem_WhenBagAndBackIsFull_ShouldAddToCharacterBag()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new(QuestComponentKind.Start, "QuestActConAcceptDoodad"),
                new(QuestComponentKind.Supply, "QuestActSupplyItem")
            );

            // Excluding sphere to avoid Update();
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);

            foreach (var questId in targetQuestIds)
            {
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, out _);

                // Add a backpack item to occupy the backpack slot (Gweonid Dyed Feathers Pack)
                SetupCharacter(mockCharacter, inventorySlots: 0, equippedBackPackItem: 31831);

                // Act
                var result = quest.Start();

                // Assert
                
                // Do not start when bag and back is full
                Assert.False(result);

                mockCharacter.Verify(c => c.SendErrorMessage(It.IsIn(ErrorMessageType.BagFull), It.IsAny<uint>(), It.IsAny<bool>()), Times.AtLeastOnce);
                var supplyItemAct = quest.Template.GetFirstComponent(QuestComponentKind.Supply).ActTemplates.OfType<QuestActSupplyItem>().FirstOrDefault();
                if (supplyItemAct is not null)
                {
                    Assert.Equal(0, mockCharacter.Object.Inventory.GetItemsCount(supplyItemAct.ItemId));
                }
            }
        }
        
        [Fact]
        public void Start_ActCheckTimer_ShouldStartSchedulerAndSendCharacterMessage()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActCheckTimer")
            );

            // Excluding sphere and acceptcomponent to avoid early exit or Update();
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);
            foreach (var questId in targetQuestIds)
            {
                // Arrange
                
                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, out _);
                SetupCharacter(mockCharacter);

                // Simulates the character to be targeting an expected npc for the quest
                var npcAcceptAct = QuestManager.Instance.GetTemplate(questId).GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();
                if (npcAcceptAct is not null)
                {
                    mockCharacter.SetupGet(o => o.CurrentTarget).Returns(new Npc { TemplateId = npcAcceptAct.NpcId });
                }
                
                // Act
                var result = quest.Start();

                // Assert
                var checkTimerAct = quest.Template.GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActCheckTimer>().FirstOrDefault();
                if (checkTimerAct is not null)
                {
                    Assert.NotNull(QuestManager.Instance.QuestTimeoutTask?[mockCharacter.Object.Id]?[questId]);
                    mockCharacter.Verify(o => o.SendMessage(It.Is<string>(s => s.Contains("quest {1} will end in {2} minutes")), It.Is<object[]>(o => o.Contains(questId) && o.Contains(checkTimerAct.LimitTime / 60000))), Times.Once);
                }
                
                //Started successfuly
                Assert.True(result);
            }
        }

        [Fact]
        public void Start_ActConAcceptNpcWhenTargetingCorrectNpc_ShouldStartQuestSuccessfuly()
        {
            /*
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptNpc")
            );

            // Excluding sphere and acceptcomponent to avoid early exit or Update();
            var questIdsToExclude = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Progress, "QuestActObjSphere")
            )
            .Union
            (
                GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptComponent")
            ));

            var targetQuestIds = questIds.Except(questIdsToExclude).ToArray();
            foreach (var questId in targetQuestIds)
            {
                // Arrange

                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _);
                SetupCharacter(mockCharacter);

                // Simulates the character to be targeting an expected npc for the quest
                var npcAcceptAct = QuestManager.Instance.GetTemplate(questId).GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();
                if (npcAcceptAct is not null)
                {
                    mockCharacter.SetupGet(o => o.CurrentTarget).Returns(new Npc { TemplateId = npcAcceptAct.NpcId });
                }

                // Act
                var result = quest.Start();

                // Assert
                var checkTimerAct = quest.Template.GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActCheckTimer>().FirstOrDefault();
                if (checkTimerAct is not null)
                {
                    Assert.NotNull(QuestManager.Instance.QuestTimeoutTask?[mockCharacter.Object.Id]?[questId]);
                    mockCharacter.Verify(o => o.SendMessage(It.Is<string>(s => s.Contains("quest {1} will end in {2} minutes")), It.Is<object[]>(o => o.Contains(questId) && o.Contains(checkTimerAct.LimitTime / 60000))), Times.Once);
                }
                Assert.True(result);
            }*/
        }


        [Fact]
        public void UseSkillAndBuff_MockedWorldManager_WhenQuestUseSkill_ShouldUseOnSelfOrTargetNpc()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptNpc", HasSkill: true)
            );
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);

            //targetQuestIds = new uint[] { 1966 };
            
            foreach (var questId in targetQuestIds)
            {
                // Arrange

                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, out var mockWorldManager);
                SetupCharacter(mockCharacter);
                // Simulates the character to be targeting an expected npc for the quest
                var npcComponent = QuestManager.Instance.GetTemplate(questId).GetFirstComponent(QuestComponentKind.Start);
                var npcAcceptAct = npcComponent.ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();


                var targetNpc = new Npc { TemplateId = npcAcceptAct.NpcId };
                var mockSkillNpc = new Mock<Npc>();
                var mockComponentNpc = new Mock<Npc>();
                mockSkillNpc.SetupAllProperties();
                mockComponentNpc.SetupAllProperties();

                if (npcAcceptAct is not null)
                {
                    mockCharacter.SetupGet(o => o.CurrentTarget).Returns(targetNpc);
                }
                mockWorldManager.Setup(wm => wm.GetNpcByTemplateId(It.IsIn(npcComponent.NpcId))).Returns(mockComponentNpc.Object);
                // Act
                var result = quest.Start();

                // Assert
                var npcAcceptActQuest = quest.Template.GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();
                var npcComponentStart = quest.Template.GetFirstComponent(QuestComponentKind.Start);
                if (npcAcceptActQuest is not null)
                {
                    Assert.Equal(QuestAcceptorType.Npc, quest.QuestAcceptorType);
                    Assert.Equal(npcAcceptActQuest.NpcId, quest.AcceptorType);

                    if (npcComponentStart.SkillSelf)
                        mockCharacter.Verify(o => o.UseSkill(It.IsIn(npcComponentStart.SkillId), It.IsIn<IUnit>(mockCharacter.Object)), Times.Once);
                    else if (npcComponentStart.NpcId > 0) 
                    {
                        mockWorldManager.Verify(o => o.GetNpcByTemplateId(It.IsIn(npcComponentStart.NpcId)), Times.Once);
                        mockComponentNpc.Verify(o => o.UseSkill(It.IsIn(npcComponentStart.SkillId), It.IsIn<IUnit>(mockComponentNpc.Object)), Times.Once);
                    }
                }
                Assert.True(result);
            }
        }

        [Fact]
        public void UseSkillAndBuff_WhenQuestUseSkill_ShouldUseOnSelfOrTargetNpc()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptNpc", HasSkill: true)
            );
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);

            //targetQuestIds = new uint[] { 1966 };

            foreach (var questId in targetQuestIds)
            {
                // Arrange

                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out _, out _, WorldManager.Instance);
                SetupCharacter(mockCharacter);

                // Simulates the character to be targeting an expected npc for the quest
                var npcComponent = QuestManager.Instance.GetTemplate(questId).GetFirstComponent(QuestComponentKind.Start);
                var npcAcceptAct = npcComponent.ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();
                var npc = WorldManager.Instance.GetNpcByTemplateId(npcComponent.NpcId);


                
                var targetNpc = new Npc { TemplateId = npcAcceptAct.NpcId };
                var mockSkillNpc = new Mock<NpcFake>(npc);
                mockSkillNpc.SetupAllProperties();
                
                if (npcAcceptAct is not null)
                {
                    mockCharacter.SetupGet(o => o.CurrentTarget).Returns(targetNpc);
                }
                if (npc is not null)
                {
                    WorldManager.Instance.SetNpc(npc.ObjId, mockSkillNpc.Object);
                }

                // Act
                var result = quest.Start();

                // Assert
                var npcAcceptActQuest = quest.Template.GetFirstComponent(QuestComponentKind.Start).ActTemplates.OfType<QuestActConAcceptNpc>().FirstOrDefault();
                var npcComponentStart = quest.Template.GetFirstComponent(QuestComponentKind.Start);
                if (npcAcceptActQuest is not null)
                {
                    Assert.Equal(QuestAcceptorType.Npc, quest.QuestAcceptorType);
                    Assert.Equal(npcAcceptActQuest.NpcId, quest.AcceptorType);

                    if (npcComponentStart.SkillSelf)
                        mockCharacter.Verify(o => o.UseSkill(It.IsIn(npcComponentStart.SkillId), It.IsIn<IUnit>(mockCharacter.Object)), Times.Once);
                    else
                    {
                        if (npc is not null)
                        {
                            mockSkillNpc.Verify(o => o.UseSkill(It.IsIn(npcComponentStart.SkillId), It.IsIn<IUnit>(mockSkillNpc.Object)), Times.Once);
                        }
                        else
                        {
                            mockSkillNpc.Verify(o => o.UseSkill(It.IsIn(npcComponentStart.SkillId), It.IsAny<IUnit>()), Times.Never);
                        }
                    }
                }
                Assert.True(result);
            }
        }

        [Fact]
        public void UseSkillAndBuff_WhenQuestUseBuff_ShouldUseOnSelfOrTargetNpc()
        {
            // Arrange
            var questIds = GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptNpc", HasBuff: true)
            );
            var targetQuestIds = RemoveSphereAndNotImplementedQuests(questIds);

            //targetQuestIds = new uint[] { 1966 };

            foreach (var questId in targetQuestIds)
            {
                // Arrange
                var mockCharacterBuffs = new Mock<IBuffs>();


                var quest = SetupQuest(questId, QuestManager.Instance, out var mockCharacter, out var mockQuestTemplate, out _, out _, out var mockSkillManager, out _, WorldManager.Instance);
                SetupCharacter(mockCharacter, 10, 0, mockCharacterBuffs);

                // Simulates the character to be targeting an expected npc for the quest
                var npcComponent = QuestManager.Instance.GetTemplate(questId).GetFirstComponent(QuestComponentKind.Start);

                // Act
                var result = quest.Start();

                // Assert
                var npcComponentStart = quest.Template.GetFirstComponent(QuestComponentKind.Start);
                
                mockCharacterBuffs.Verify(o => o.AddBuff(It.Is<Buff>(b => 
                    b.Owner.Id == mockCharacter.Object.Id && 
                    b.SkillCaster.Type == SkillCasterType.Unit), It.IsAny<uint>(), It.IsAny<int>()), Times.Once);
                mockSkillManager.Verify(sm => sm.GetBuffTemplate(It.IsIn(npcComponentStart.BuffId)), Times.Once);
                
                Assert.True(result);
            }
        }
        private IEnumerable<uint> RemoveSphereAndNotImplementedQuests(IEnumerable<uint> questIds)
        {
            // Excluding sphere and acceptcomponent to avoid early exit or Update();
            return questIds
            .Except(
                GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Progress, "QuestActObjSphere")
            )
            .Union
            (
                GetQuestIdsWithComponentKindContainingActDetailType(
                new QuestCondition(QuestComponentKind.Start, "QuestActConAcceptComponent")
            )));
        }

        private void SetupCharacter(Mock<ICharacter> mockCharacter, byte inventorySlots = 10, uint equippedBackPackItem = 0,  Mock<IBuffs> mockCharacterBuffs = null)
        {
            int randomCharacterId = Random.Shared.Next(1, int.MaxValue);
            string randomCharacterName = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            mockCharacter.SetupAllProperties();
            if (mockCharacterBuffs is not null)
            {
                mockCharacter.SetupGet(o => o.Buffs).Returns(mockCharacterBuffs.Object);
            }
            mockCharacter.SetupGet(o => o.Name).Returns(randomCharacterName);
            mockCharacter.SetupGet(o => o.Id).Returns((uint)randomCharacterId);
            mockCharacter.SetupGet(o => o.NumInventorySlots).Returns(inventorySlots);

            var inventory = new Inventory(mockCharacter.Object);
            mockCharacter.SetupGet(o => o.Inventory).Returns(inventory);
            if (equippedBackPackItem > 0)
            {
                inventory.TryEquipNewBackPack(AAEmu.Game.Models.Game.Items.Actions.ItemTaskType.Gm, equippedBackPackItem, 1);
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
            out Mock<IExpressTextManager> mockExpressTextManager,
            out Mock<IWorldManager> mockWorldManager)
        {
            mockCharacter = new Mock<ICharacter>();
            mockQuestTemplate = new Mock<IQuestTemplate>();
            mockSphereQuestManager = new Mock<ISphereQuestManager>();
            mockExpressTextManager = new Mock<IExpressTextManager>();
            mockSkillManager = new Mock<ISkillManager>();
            mockTaskManager = new Mock<ITaskManager>();
            mockWorldManager = new Mock<IWorldManager>();

            var quest = new Quest(
                questManager.GetTemplate(questId),
                questManager,
                mockSphereQuestManager.Object,
                mockTaskManager.Object,
                mockSkillManager.Object,
                mockExpressTextManager.Object,
                mockWorldManager.Object);

            quest.Owner = mockCharacter.Object;

            SetupCharacter(mockCharacter);
            return quest;
        }

        private Quest SetupQuest(
            uint questId,
            IQuestManager questManager,
            out Mock<ICharacter> mockCharacter,
            out Mock<IQuestTemplate> mockQuestTemplate,
            out Mock<ISphereQuestManager> mockSphereQuestManager,
            out Mock<ITaskManager> mockTaskManager,
            out Mock<ISkillManager> mockSkillManager,
            out Mock<IExpressTextManager> mockExpressTextManager,
            IWorldManager worldManager)
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
                mockExpressTextManager.Object,
                worldManager);

            quest.Owner = mockCharacter.Object;

            SetupCharacter(mockCharacter);
            return quest;
        }
        
        public IEnumerable<uint> GetQuestIdsWithComponentKindContainingActDetailType(params QuestCondition[] detailTypes)
        {
            List<uint> questIds = new();

            if (detailTypes.Length == 0)
            {
                return questIds;
            }
            
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    var sqlFrom = new StringBuilder();
                    var sqlWhere = new StringBuilder();
                    var detailCount = 1;
                    foreach (var questCondition in detailTypes)
                    {
                        sqlFrom.Append($@"
                                            inner join quest_components qc{detailCount}
                                                on qc{detailCount}.quest_context_id = qc.quest_context_id
                                            inner join quest_acts qa{detailCount}
                                                on qc{detailCount}.id = qa{detailCount}.quest_component_id");

                        sqlWhere.Append($@"
                                            and qc{detailCount}.component_kind_id = {(int)questCondition.ComponentKind} 
                                            and qa{detailCount}.act_detail_type = '{questCondition.DetailType}'");
                        if (questCondition.HasSkillSelf.HasValue)
                        {
                            sqlWhere.Append($@"
                                                and qc{detailCount}.skill_self = {(questCondition.HasSkillSelf.Value ? "t" : "f")}");
                        }
                        if (questCondition.HasSkill.HasValue)
                        {
                            sqlWhere.Append($@"
                                                and qc{detailCount}.skill_id {(questCondition.HasSkill.Value ? ">" : "=")} 0");
                        }
                        if (questCondition.HasBuff.HasValue)
                        {
                            sqlWhere.Append($@"
                                                and qc{detailCount}.buff_id {(questCondition.HasBuff.Value ? ">" : "=")} 0");
                        }
                        detailCount++;
                    }
                        
                    command.CommandText = $@"select DISTINCT
                                                qc.quest_context_id
                                            from
                                                quest_components qc
                                                {sqlFrom}
                                            where
                                                qc.id == qc1.id
                                                {sqlWhere}
                                            order by
                                                qc.quest_context_id";

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

        public record QuestCondition(QuestComponentKind ComponentKind, string DetailType, bool? HasSkill = null, bool? HasBuff = null, bool? HasSkillSelf = null);
        public class NpcFake : Npc
        {
            public NpcFake(Npc wrapped)
            {
                this.Name = wrapped.Name;
                this.Id = wrapped.Id;
                this.ObjId = wrapped.ObjId;
                this.Template = wrapped.Template;
                this.TemplateId = wrapped.TemplateId;
            }
        }
    }
}
