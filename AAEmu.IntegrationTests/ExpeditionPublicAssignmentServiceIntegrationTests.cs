using System.Reflection;
using System.Text.Json;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using Moq;
using Xunit;

namespace AAEmu.IntegrationTests;

[Collection(ExpeditionCoreStaticCollection.Name)]
public sealed class ExpeditionPublicAssignmentServiceIntegrationTests(ExpeditionRecruitmentMySqlFixture fixture)
    : IClassFixture<ExpeditionRecruitmentMySqlFixture>
{
    private const uint ExpeditionId = 940001;
    private const uint CurrentContributorId = 940101;
    private const uint EarlierContributorId = 940102;
    private const uint BystanderId = 940103;
    private const uint QuestId = 940201;
    private const uint ItemId = 940301;
    private const uint Reward = 1600;

    [Fact]
    public async Task Completion_CommitsGuildExpContributorBalancesClaimsAndMailStagingOnce()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        var context = CreateContext(forceCompletionSqlFailure: false);
        try
        {
            context.Service.Load();
            context.Service.OnCharacterLogin(context.CurrentContributor);

            Assert.True(context.Service.TryEnqueue(context.CurrentContributor,
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 1 }));
            await WaitForQueue(context.Service);

            using var connection = fixture.Open();
            Assert.Equal(Reward, ScalarUInt(connection, "SELECT exp FROM expeditions WHERE id=@id"));
            Assert.Equal(Reward, MemberContribution(connection, CurrentContributorId));
            Assert.Equal(Reward, MemberContribution(connection, EarlierContributorId));
            Assert.Equal(0u, MemberContribution(connection, BystanderId));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM expedition_public_assignment_claims WHERE expedition_id=@id"));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM public_assignment_reward_probe WHERE expedition_id=@id"));
            Assert.Equal(1u, ScalarUInt(connection,
                "SELECT guild_rewarded FROM expedition_public_assignments WHERE expedition_id=@id"));

            Assert.False(context.Service.TryEnqueue(context.CurrentContributor,
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 1 }));
            await Task.Delay(50);

            Assert.Equal(Reward, ScalarUInt(connection, "SELECT exp FROM expeditions WHERE id=@id"));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM expedition_public_assignment_claims WHERE expedition_id=@id"));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM public_assignment_reward_probe WHERE expedition_id=@id"));
        }
        finally
        {
            context.Service.Dispose();
            ResetGameData();
        }
    }

    [Fact]
    public async Task Completion_WhenFinalStateWriteFails_RollsBackEveryRewardAndRemainsReplayable()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        var context = CreateContext(forceCompletionSqlFailure: true);
        try
        {
            context.Service.Load();
            context.Service.OnCharacterLogin(context.CurrentContributor);

            Assert.True(context.Service.TryEnqueue(context.CurrentContributor,
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 1 }));
            await WaitForQueue(context.Service);

            using var connection = fixture.Open();
            Assert.Equal(0u, ScalarUInt(connection, "SELECT exp FROM expeditions WHERE id=@id"));
            Assert.Equal(0u, MemberContribution(connection, CurrentContributorId));
            Assert.Equal(0u, MemberContribution(connection, EarlierContributorId));
            Assert.Equal(0u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM expedition_public_assignment_claims WHERE expedition_id=@id"));
            Assert.Equal(0u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM public_assignment_reward_probe WHERE expedition_id=@id"));
            Assert.Equal((uint)TodayAssignmentStatus.Progress, ScalarUInt(connection,
                "SELECT status FROM expedition_public_assignments WHERE expedition_id=@id"));
            Assert.Equal(1u, ScalarUInt(connection,
                "SELECT version FROM expedition_public_assignments WHERE expedition_id=@id"));
            Assert.Equal(0u, ScalarUInt(connection,
                $"SELECT COUNT(*) FROM expedition_public_assignment_contributors WHERE expedition_id=@id AND character_id={CurrentContributorId}"));

            using (var drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TRIGGER fail_public_assignment_completion";
                drop.ExecuteNonQuery();
            }
            Assert.True(context.Service.TryEnqueue(context.CurrentContributor,
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 1 }));
            await WaitForQueue(context.Service);

            Assert.Equal(Reward, ScalarUInt(connection, "SELECT exp FROM expeditions WHERE id=@id"));
            Assert.Equal(Reward, MemberContribution(connection, CurrentContributorId));
            Assert.Equal(Reward, MemberContribution(connection, EarlierContributorId));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM expedition_public_assignment_claims WHERE expedition_id=@id"));
            Assert.Equal(2u, ScalarUInt(connection,
                "SELECT COUNT(*) FROM public_assignment_reward_probe WHERE expedition_id=@id"));
        }
        finally
        {
            context.Service.Dispose();
            ResetGameData();
        }
    }

    [Fact]
    public async Task Completion_AfterRestartWithPriorWeekContribution_UsesPersistedValuesForScopedUpdate()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        var priorWeek = ServerCalendar.WeekStartMondayUtc.AddDays(-7);
        var context = CreateContext(forceCompletionSqlFailure: false, priorWeeklyContribution: 700,
            priorWeeklyPeriodStart: priorWeek);
        try
        {
            context.Service.Load();
            context.Service.OnCharacterLogin(context.CurrentContributor);

            Assert.True(context.Service.TryEnqueue(context.CurrentContributor,
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 1 }));
            await WaitForQueue(context.Service);

            using var connection = fixture.Open();
            Assert.Equal(Reward, MemberContribution(connection, CurrentContributorId));
            Assert.Equal(Reward, MemberWeeklyContribution(connection, CurrentContributorId));
            Assert.Equal(ServerCalendar.WeekStartMondayUtc.Date,
                MemberWeeklyPeriodStart(connection, CurrentContributorId).Date);
        }
        finally
        {
            context.Service.Dispose();
            ResetGameData();
        }
    }

    private TestContext CreateContext(bool forceCompletionSqlFailure, uint priorWeeklyContribution = 0,
        DateTime? priorWeeklyPeriodStart = null)
    {
        ResetDatabase();
        var period = ExpeditionPublicAssignmentService.GetPeriodStart(ServerCalendar.UtcNow, 1);
        var step = new TodayQuestStepTemplate
        {
            Id = 940011, RealStep = 13, SortId = TodayQuestStepTemplate.ExpeditionPublicBoardSortId,
            LevelMin = 1, LevelMax = 8
        };
        var group = new TodayQuestGroupTemplate { Id = 940012, StepId = step.Id, AutomaticRestart = false };
        group.QuestContextIds.Add(QuestId);
        step.Groups.Add(group);
        TodayQuestGameData.Instance.SetStepsForTest(step);
        ExpeditionLevelGameData.Instance.SetForTest(
            new ExpeditionLevel { Id = 1, TotalExp = 0, DailyExp = 100_000 },
            new ExpeditionLevel { Id = 2, TotalExp = 100_000, DailyExp = 100_000 });

        var repository = new FixturePublicAssignmentRepository(fixture);
        using (var connection = fixture.Open())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction,
                "INSERT INTO expeditions(id,owner,owner_name,name,mother,level,exp,daily_exp,last_exp_update_time) VALUES(@id,@owner,'Current','Public Test',101,1,0,0,@updated)",
                ("@owner", CurrentContributorId), ("@updated", ServerCalendar.UtcNow));
            foreach (var member in new[]
                     {
                         (CurrentContributorId, "Current"), (EarlierContributorId, "Earlier"),
                         (BystanderId, "Bystander")
                     })
                Execute(connection, transaction,
                    "INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point,weekly_contribution_point,weekly_contribution_period_start) VALUES(@character,@id,@name,55,1,@leave,2,3,4,'',0,@weekly,@week)",
                    ("@character", member.Item1), ("@name", member.Item2), ("@leave", ServerCalendar.UtcNow),
                    ("@weekly", priorWeeklyContribution),
                    ("@week", priorWeeklyPeriodStart ?? ServerCalendar.WeekStartMondayUtc));
            var state = new ExpeditionPublicAssignmentState
            {
                ExpeditionId = ExpeditionId, PeriodStart = period, RealStep = step.RealStep,
                GroupId = group.Id, QuestContextId = QuestId, Status = TodayAssignmentStatus.Progress
            };
            Assert.True(repository.TrySave(connection, transaction, state, 0));
            repository.UpsertContributor(connection, transaction, state, EarlierContributorId, "Earlier", 1);
            transaction.Commit();
        }
        if (forceCompletionSqlFailure)
        {
            using var connection = fixture.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_public_assignment_completion BEFORE UPDATE ON expedition_public_assignments FOR EACH ROW BEGIN IF NEW.status <> 0 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced completion failure'; END IF; END";
            command.ExecuteNonQuery();
        }

        var expedition = new Expedition
        {
            Id = (FactionsEnum)ExpeditionId, OwnerId = CurrentContributorId, OwnerName = "Current",
            Name = "Public Test", MotherId = (FactionsEnum)101, Level = 1, Exp = 0, DailyExp = 0,
            LastExpUpdateTime = ServerCalendar.UtcNow,
            Members =
            [
                Member(CurrentContributorId, "Current", priorWeeklyContribution, priorWeeklyPeriodStart),
                Member(EarlierContributorId, "Earlier", priorWeeklyContribution, priorWeeklyPeriodStart),
                Member(BystanderId, "Bystander", priorWeeklyContribution, priorWeeklyPeriodStart)
            ]
        };
        var current = Character(CurrentContributorId, "Current", expedition);
        var world = new Mock<IWorldManager>();
        world.Setup(x => x.GetCharacterById(CurrentContributorId)).Returns(current);
        world.Setup(x => x.GetCharacterById(It.Is<uint>(id => id != CurrentContributorId))).Returns((Character)null);
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object, world.Object, new Mock<IChatManager>().Object, fixture,
            new Mock<IItemManager>().Object, new Mock<IFactionManager>().Object);
        typeof(ExpeditionManager).GetField("_expeditions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<FactionsEnum, Expedition> { [expedition.Id] = expedition });
        var quests = new Mock<IQuestManager>();
        quests.Setup(x => x.GetTemplate(QuestId)).Returns(QuestTemplate());
        var delivery = new ProbeRewardDelivery();
        var service = new ExpeditionPublicAssignmentService(repository, manager, world.Object, quests.Object, delivery,
            Mock.Of<IGameDataManager>());
        return new TestContext(service, current);
    }

    private void ResetDatabase()
    {
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS fail_public_assignment_completion;
            CREATE TABLE IF NOT EXISTS public_assignment_reward_probe (
                expedition_id INT NOT NULL, character_id INT UNSIGNED NOT NULL,
                PRIMARY KEY(expedition_id,character_id));
            SET FOREIGN_KEY_CHECKS=0;
            TRUNCATE public_assignment_reward_probe;
            TRUNCATE expedition_public_assignment_claims;
            TRUNCATE expedition_public_assignment_contributors;
            TRUNCATE expedition_public_assignments;
            TRUNCATE expedition_members;
            TRUNCATE expeditions;
            SET FOREIGN_KEY_CHECKS=1;
            """;
        command.ExecuteNonQuery();
    }

    private static void ResetGameData()
    {
        TodayQuestGameData.Instance.SetStepsForTest();
        ExpeditionLevelGameData.Instance.SetForTest();
    }

    private static QuestTemplate QuestTemplate()
    {
        var template = new QuestTemplate { Id = QuestId, Name = "Public assignment", DetailId = QuestDetail.Expedition };
        var progress = new QuestComponentTemplate(template) { Id = 1, KindId = QuestComponentKind.Progress };
        progress.ActTemplates.Add(new QuestActObjGainHonorPoint(progress)
        {
            Count = 1, ThisComponentObjectiveIndex = 0
        });
        var reward = new QuestComponentTemplate(template) { Id = 2, KindId = QuestComponentKind.Reward };
        reward.ActTemplates.Add(new QuestActConAutoComplete(reward));
        reward.ActTemplates.Add(new QuestActSupplyItem(reward) { ItemId = ItemId, Count = 1 });
        reward.ActTemplates.Add(new QuestActSupplyExpeditionExp(reward) { Point = Reward });
        reward.ActTemplates.Add(new QuestActSupplyContributionPoint(reward) { Point = Reward });
        reward.ActTemplates.Add(new QuestActSupplyExp(reward) { Exp = 0 });
        reward.ActTemplates.Add(new QuestActSupplyCopper(reward) { Amount = 0 });
        template.Components[progress.Id] = progress;
        template.Components[reward.Id] = reward;
        return template;
    }

    private static ExpeditionMember Member(uint id, string name, uint weeklyContribution = 0,
        DateTime? weeklyPeriodStart = null) => new()
    {
        ExpeditionId = (FactionsEnum)ExpeditionId, CharacterId = id, Name = name, Level = 55, Role = 1,
        ContributionPoint = 0, WeeklyContributionPoint = weeklyContribution,
        WeeklyContributionPeriodStart = weeklyPeriodStart ?? ServerCalendar.WeekStartMondayUtc
    };

    private static Character Character(uint id, string name, Expedition expedition)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, Name = name, Expedition = expedition };
        character.Quests = new CharacterQuests(character);
        character.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = character };
        return character;
    }

    private static async Task WaitForQueue(ExpeditionPublicAssignmentService service)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (service.QueueDepth != 0 && DateTime.UtcNow < timeout)
            await Task.Delay(10);
        Assert.Equal(0, service.QueueDepth);
    }

    private static uint MemberContribution(MySqlConnection connection, uint characterId) => ScalarUInt(connection,
        $"SELECT contribution_point FROM expedition_members WHERE expedition_id=@id AND character_id={characterId}");

    private static uint MemberWeeklyContribution(MySqlConnection connection, uint characterId) => ScalarUInt(connection,
        $"SELECT weekly_contribution_point FROM expedition_members WHERE expedition_id=@id AND character_id={characterId}");

    private static DateTime MemberWeeklyPeriodStart(MySqlConnection connection, uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT weekly_contribution_period_start FROM expedition_members WHERE expedition_id=@id AND character_id={characterId}";
        command.Parameters.AddWithValue("@id", ExpeditionId);
        return Convert.ToDateTime(command.ExecuteScalar());
    }

    private static uint ScalarUInt(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", ExpeditionId);
        return Convert.ToUInt32(command.ExecuteScalar());
    }

    private static void Execute(MySqlConnection connection, MySqlTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", ExpeditionId);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private sealed record TestContext(ExpeditionPublicAssignmentService Service, Character CurrentContributor);

    private sealed class ProbeRewardDelivery : IPublicQuestRewardDeliveryService
    {
        public bool TryStageRewards(PublicQuestRewardBundle bundle,
            IReadOnlyCollection<PublicQuestRewardRecipient> recipients, MySqlConnection connection,
            MySqlTransaction transaction, out PublicQuestStagedRewardDelivery delivery)
        {
            foreach (var recipient in recipients)
                Execute(connection, transaction,
                    "INSERT INTO public_assignment_reward_probe(expedition_id,character_id) VALUES(@id,@character)",
                    ("@character", recipient.CharacterId));
            var constructor = typeof(PublicQuestStagedRewardDelivery).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic).Single();
            delivery = (PublicQuestStagedRewardDelivery)constructor.Invoke(
                [Mock.Of<IMailManager>(), Mock.Of<IItemManager>(), Array.Empty<BaseMail>(), Array.Empty<Item>()]);
            return true;
        }
    }

    private sealed class FixturePublicAssignmentRepository(ExpeditionRecruitmentMySqlFixture fixture)
        : IExpeditionPublicAssignmentRepository
    {
        private readonly MySqlExpeditionPublicAssignmentRepository _inner = new();
        public MySqlConnection Open() => fixture.Open();

        public IReadOnlyList<ExpeditionPublicAssignmentState> LoadCurrent(DateTime periodStart)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT expedition_id,period_start,real_step,group_id,quest_context_id,status,objectives,version,completed_at,guild_rewarded,selection_generation FROM expedition_public_assignments WHERE period_start=@period";
            command.Parameters.AddWithValue("@period", periodStart);
            using var reader = command.ExecuteReader();
            var states = new List<ExpeditionPublicAssignmentState>();
            while (reader.Read())
            {
                var state = new ExpeditionPublicAssignmentState
                {
                    ExpeditionId = reader.GetUInt32(0), PeriodStart = reader.GetDateTime(1),
                    RealStep = reader.GetUInt32(2), GroupId = reader.GetUInt32(3),
                    QuestContextId = reader.GetUInt32(4),
                    Status = (TodayAssignmentStatus)Convert.ToSByte(reader.GetValue(5)),
                    Version = reader.GetUInt32(7), CompletedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    GuildRewarded = reader.GetBoolean(9), SelectionGeneration = reader.GetUInt32(10)
                };
                var objectives = JsonSerializer.Deserialize<int[]>(reader.GetString(6)) ?? [];
                Array.Copy(objectives, state.Objectives, Math.Min(objectives.Length, state.Objectives.Length));
                states.Add(state);
            }
            reader.Close();
            foreach (var state in states)
            {
                using var contributors = connection.CreateCommand();
                contributors.CommandText = "SELECT character_id,character_name,contribution FROM expedition_public_assignment_contributors WHERE expedition_id=@id AND period_start=@period AND real_step=@step";
                contributors.Parameters.AddWithValue("@id", state.ExpeditionId);
                contributors.Parameters.AddWithValue("@period", state.PeriodStart);
                contributors.Parameters.AddWithValue("@step", state.RealStep);
                using var rows = contributors.ExecuteReader();
                while (rows.Read()) state.Contributors[rows.GetUInt32(0)] =
                    new PublicAssignmentContributor(rows.GetUInt32(0), rows.GetString(1), rows.GetUInt64(2));
            }
            return states;
        }

        public bool TrySave(MySqlConnection connection, MySqlTransaction transaction,
            ExpeditionPublicAssignmentState state, uint expectedVersion) =>
            _inner.TrySave(connection, transaction, state, expectedVersion);

        public void UpsertContributor(MySqlConnection connection, MySqlTransaction transaction,
            ExpeditionPublicAssignmentState state, uint characterId, string characterName, int delta) =>
            _inner.UpsertContributor(connection, transaction, state, characterId, characterName, delta);
    }
}
