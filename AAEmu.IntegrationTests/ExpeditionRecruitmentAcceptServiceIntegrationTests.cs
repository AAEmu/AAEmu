using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using Moq;
using System.Reflection;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class ExpeditionRecruitmentControlledTransitionIntegrationTests(ExpeditionRecruitmentMySqlFixture fixture)
    : IClassFixture<ExpeditionRecruitmentMySqlFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private readonly Dictionary<uint, Character> _sessions = [];
    private readonly Dictionary<FactionsEnum, Expedition> _expeditions = [];

    [Fact]
    public void OfflineAccept_CommitsMembershipAndDeletesAllApplications()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1, 2);
        var coordinator = new ControlledJoinCoordinator();
        var service = Service(coordinator);

        Assert.Equal(ExpeditionRecruitmentResult.Success, service.Accept(Actor(1), 10));

        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
        Assert.True(coordinator.LastTransition!.Committed);
    }

    [Fact]
    public void OnlineAccept_UsesSameRepositoryTransactionAndCommits()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        var online = new Character(new()) { Id = 10 };
        var coordinator = new ControlledJoinCoordinator();
        var service = Service(coordinator, online);

        Assert.Equal(ExpeditionRecruitmentResult.Success, service.Accept(Actor(1), 10));

        Assert.True(coordinator.OnlinePathUsed);
        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
    }

    [Fact]
    public void PersistenceFailure_RollsBackMembershipAndPreservesApplication()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        var coordinator = new ControlledJoinCoordinator { FailAfterMembershipWrite = true };
        var service = Service(coordinator);

        Assert.Throws<InvalidOperationException>(() => service.Accept(Actor(1), 10));

        Assert.Equal(0, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
        Assert.True(coordinator.LastTransition!.Disposed);
        Assert.False(coordinator.LastTransition.Committed);
    }

    [Fact]
    public async Task CompetingGuildAccepts_CommitExactlyOneMembership()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1, 2);
        var first = Service(new ControlledJoinCoordinator());
        var second = Service(new ControlledJoinCoordinator());

        var results = await Task.WhenAll(Task.Run(() => first.Accept(Actor(1), 10)),
            Task.Run(() => second.Accept(Actor(2), 10)));

        Assert.Single(results, x => x == ExpeditionRecruitmentResult.Success);
        Assert.Contains(Scalar("SELECT expedition_id FROM characters WHERE id=10"), new long[] { 1, 2 });
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
    }

    [Fact]
    public void RegistrationDebitFailure_RollsBackRecruitmentAndLeavesMemoryUnchanged()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Execute("DELETE FROM expedition_recruitment_applications; DELETE FROM expedition_recruitments");
        var actor = Actor(1, 10);
        actor.Money = 100000;
        var service = Service(new ControlledJoinCoordinator());

        Assert.Equal(ExpeditionRecruitmentResult.NotEnoughMoney,
            service.Register(actor, 1, 3, "open", Now));

        Assert.Equal(100000, actor.Money);
        Assert.Equal(0, Scalar("SELECT money FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_recruitments WHERE expedition_id=1"));
    }

    [Fact]
    public void RegistrationSuccess_DebitsScopedColumnAndPreservesUnrelatedCharacterFields()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Execute("UPDATE characters SET money=250000 WHERE id=10");
        var actor = Actor(1, 10);
        actor.Money = 250000;
        var service = Service(new ControlledJoinCoordinator());

        Assert.Equal(ExpeditionRecruitmentResult.Success,
            service.Register(actor, 1, 3, "open", Now));

        Assert.Equal(150000, actor.Money);
        Assert.Equal(150000, Scalar("SELECT money FROM characters WHERE id=10"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_recruitments WHERE expedition_id=1"));
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_id,name,level,faction_id,ability1,ability2,ability3,expedition_id FROM characters WHERE id=10";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1u, reader.GetUInt32(0));
        Assert.Equal("Applicant", reader.GetString(1));
        Assert.Equal(55, reader.GetByte(2));
        Assert.Equal(1u, reader.GetUInt32(3));
        Assert.Equal(new byte[] { 2, 3, 4 }, new[] { reader.GetByte(4), reader.GetByte(5), reader.GetByte(6) });
        Assert.Equal(0, reader.GetInt32(7));
    }

    [Fact]
    public void Register_RejectsActorWhoseSessionChangesBeforeLockedRevalidation()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Execute("DELETE FROM expedition_recruitment_applications; DELETE FROM expedition_recruitments; UPDATE characters SET money=250000 WHERE id=10");
        var actor = Actor(1, 10);
        actor.Money = 250000;
        var service = Service(new ControlledJoinCoordinator(), invalidateAfterFirstLookup: actor);

        Assert.Equal(ExpeditionRecruitmentResult.NotAuthorized, service.Register(actor, 1, 3, "open", Now));
        Assert.Equal(250000, Scalar("SELECT money FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_recruitments"));
    }

    [Fact]
    public void ApplyAndWithdraw_RejectStaleCharacterSessionWithoutChangingApplication()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        var character = CurrentCharacter(10);
        var service = Service(new ControlledJoinCoordinator(), invalidateAfterFirstLookup: character);

        Assert.Equal(ExpeditionRecruitmentResult.NotAuthorized, service.Withdraw(character, 1));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));

        character = CurrentCharacter(10);
        service = Service(new ControlledJoinCoordinator(), invalidateAfterFirstLookup: character);
        Assert.Equal(ExpeditionRecruitmentResult.NotAuthorized, service.Apply(character, 1, "new", Now));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
    }

    [Fact]
    public void Accept_RejectsOfficerWhoseSessionChangesBeforeTransitionPersistence()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        var actor = Actor(1);
        var service = Service(new ControlledJoinCoordinator(), invalidateAfterFirstLookup: actor);

        Assert.Equal(ExpeditionRecruitmentResult.NotAuthorized, service.Accept(actor, 10));
        Assert.Equal(0, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_recruitment_applications WHERE character_id=10"));
    }

    [Fact]
    public void RealOfflineJoin_LoginLeaseFirst_SerializesThenCommitsMembership()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Character live = null;
        var actor = Actor(1);
        var (manager, _) = RealManager(actor, () => live);
        var candidate = new Character(new()) { Id = 10 };
        using var loginConnection = fixture.Open();
        var loginLease = manager.BeginCharacterLoginAssociation(candidate, loginConnection);
        var snapshot = new ExpeditionJoinCandidate(10, 1, "Applicant", 55, 0, (FactionsEnum)1, 2, 3, 4, 0, 0);
        Exception joinError = null;
        var joined = false;
        using var joinEntered = new ManualResetEventSlim();
        using var joinFinished = new ManualResetEventSlim();
        var join = new Thread(() =>
        {
            try
            {
                joinEntered.Set();
                joined = manager.TryBeginMemberJoin(actor, snapshot, actor.Expedition,
                    FactionsEnum.NuiaAlliance, out var transition);
                if (!joined)
                    return;
                using (transition)
                using (var connection = fixture.Open())
                using (var transaction = connection.BeginTransaction())
                {
                    transition.Persist(connection, transaction);
                    transaction.Commit();
                    transition.Commit();
                }
            }
            catch (Exception error)
            {
                joinError = error;
            }
            finally
            {
                joinFinished.Set();
            }
        }) { IsBackground = true };
        join.Start();
        var joinStarted = false;
        var wasBlocked = false;
        var joinCompleted = false;
        var joinJoined = false;
        try
        {
            joinStarted = joinEntered.Wait(TimeSpan.FromSeconds(1));
            wasBlocked = !joinFinished.Wait(TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            loginLease.Dispose();
            joinCompleted = joinFinished.Wait(TimeSpan.FromSeconds(5));
            joinJoined = join.Join(TimeSpan.FromSeconds(1));
        }
        Assert.True(joinStarted);
        Assert.True(wasBlocked);
        Assert.True(joinCompleted);
        Assert.True(joinJoined);
        Assert.Null(joinError);
        Assert.True(joined);
        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
    }

    [Fact]
    public void RealOfflineJoin_CommitFirst_BlocksLoginUntilPublicationThenAssociatesFromDatabase()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Character live = null;
        var actor = Actor(1);
        var (manager, _) = RealManager(actor, () => live);
        var snapshot = new ExpeditionJoinCandidate(10, 1, "Applicant", 55, 0, (FactionsEnum)1, 2, 3, 4, 0, 0);
        Assert.True(manager.TryBeginMemberJoin(actor, snapshot, actor.Expedition,
            FactionsEnum.NuiaAlliance, out var transition));
        var candidate = new Character(new()) { Id = 10 };
        Exception loginError = null;
        var associated = false;
        using var loginEntered = new ManualResetEventSlim();
        using var loginFinished = new ManualResetEventSlim();
        Thread login = null;
        var loginStarted = false;
        var wasBlocked = false;
        var loginCompleted = false;
        var loginJoined = false;
        try
        {
            using (var connection = fixture.Open())
            using (var transaction = connection.BeginTransaction())
            {
                transition.Persist(connection, transaction);
                transaction.Commit();
            }
            login = new Thread(() =>
            {
                try
                {
                    loginEntered.Set();
                    using var loginConnection = fixture.Open();
                    using var loginLease = manager.BeginCharacterLoginAssociation(candidate, loginConnection);
                    live = candidate; // mirrors World registration while the production membership lease is held
                    associated = ReferenceEquals(actor.Expedition, candidate.Expedition);
                }
                catch (Exception error)
                {
                    loginError = error;
                }
                finally
                {
                    loginFinished.Set();
                }
            }) { IsBackground = true };
            login.Start();
            loginStarted = loginEntered.Wait(TimeSpan.FromSeconds(1));
            wasBlocked = !loginFinished.Wait(TimeSpan.FromMilliseconds(100));
            transition.Commit();
        }
        finally
        {
            transition.Dispose();
            if (login != null)
            {
                loginCompleted = loginFinished.Wait(TimeSpan.FromSeconds(5));
                loginJoined = login.Join(TimeSpan.FromSeconds(1));
            }
        }
        Assert.True(loginStarted);
        Assert.True(wasBlocked);
        Assert.True(loginCompleted);
        Assert.True(loginJoined);
        Assert.Null(loginError);
        Assert.True(associated);
        Assert.Same(actor.Expedition, candidate.Expedition);
        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
    }

    [Fact]
    public void RealDeletionGuard_BlocksLoginAssociationAndIdentifiesOwner()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed(1);
        Execute("UPDATE characters SET expedition_id=1 WHERE id=10");
        var actor = Actor(1);
        actor.Expedition.OwnerId = actor.Id;
        actor.Expedition.Members.Add(new ExpeditionMember
        {
            ExpeditionId = actor.Expedition.Id,
            CharacterId = 10,
            Name = "Applicant",
            Role = 0,
            Memo = string.Empty,
            LastWorldLeaveTime = DateTime.UnixEpoch,
            WeeklyContributionPeriodStart = DateTime.UnixEpoch
        });
        using (var memberConnection = fixture.Open())
        using (var memberTransaction = memberConnection.BeginTransaction())
        {
            actor.Expedition.Members[^1].Save(memberConnection, memberTransaction);
            memberTransaction.Commit();
        }
        Character registered = null;
        var (manager, _) = RealManager(actor, () => registered);
        using (var ownerGuard = manager.BeginCharacterDeletionGuard(actor.Id))
            Assert.True(ownerGuard.IsOwner);
        var candidate = new Character(new()) { Id = 10 };
        var guard = manager.BeginCharacterDeletionGuard(10);
        Assert.False(guard.IsOwner);
        Exception loginError = null;
        var associated = false;
        using var loginEntered = new ManualResetEventSlim();
        using var loginFinished = new ManualResetEventSlim();
        Thread login = null;
        var loginStarted = false;
        var wasBlocked = false;
        var loginCompleted = false;
        var loginJoined = false;
        try
        {
            using var lockedConnection = fixture.Open();
            using var lockedTransaction = lockedConnection.BeginTransaction();
            using (var row = lockedConnection.CreateCommand())
            {
                row.Transaction = lockedTransaction;
                row.CommandText = "SELECT expedition_id FROM characters WHERE id=10 FOR UPDATE";
                Assert.Equal(1, Convert.ToInt32(row.ExecuteScalar()));
            }
            login = new Thread(() =>
            {
                try
                {
                    loginEntered.Set();
                    using var connection = fixture.Open();
                    using var lease = manager.BeginCharacterLoginAssociation(candidate, connection);
                    registered = candidate;
                    associated = ReferenceEquals(actor.Expedition, candidate.Expedition);
                }
                catch (Exception error)
                {
                    loginError = error;
                }
                finally
                {
                    loginFinished.Set();
                }
            }) { IsBackground = true };
            login.Start();
            loginStarted = loginEntered.Wait(TimeSpan.FromSeconds(1));
            wasBlocked = !loginFinished.Wait(TimeSpan.FromMilliseconds(100));
            lockedTransaction.Commit();
        }
        finally
        {
            guard.Dispose();
            if (login != null)
            {
                loginCompleted = loginFinished.Wait(TimeSpan.FromSeconds(5));
                loginJoined = login.Join(TimeSpan.FromSeconds(1));
            }
        }
        Assert.True(loginStarted);
        Assert.True(wasBlocked);
        Assert.True(loginCompleted);
        Assert.True(loginJoined);
        Assert.Null(loginError);
        Assert.True(associated);
        Assert.Same(actor.Expedition, candidate.Expedition);
    }

    private ExpeditionRecruitmentService Service(ControlledJoinCoordinator coordinator, Character online = null,
        Character invalidateAfterFirstLookup = null)
    {
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object, new Mock<ITeamManager>().Object,
            new Mock<IWorldManager>().Object, new Mock<IChatManager>().Object, fixture,
            new Mock<IItemManager>().Object, Factions());
        var config = (Dictionary<string, long>)typeof(ExpeditionManager)
            .GetField("_contentConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        config["expedition_recruit_period_min"] = 3;
        config["expedition_recruit_period_min_cost"] = 100000;
        config["expedition_recruit_period_max"] = 9;
        config["expedition_recruit_period_max_cost"] = 200000;
        config["expedition_recruit_apply_max"] = 5;
        typeof(ExpeditionManager).GetField("_expeditions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, _expeditions);
        var world = new Mock<IWorldManager>();
        if (online != null)
        {
            AttachConnection(online);
            _sessions[online.Id] = online;
        }
        var invalidated = false;
        world.Setup(x => x.GetCharacterById(It.IsAny<uint>())).Returns((uint id) =>
        {
            var current = _sessions.GetValueOrDefault(id);
            if (!invalidated && invalidateAfterFirstLookup != null && current != null &&
                ReferenceEquals(current, invalidateAfterFirstLookup))
            {
                invalidated = true;
                var replacement = new Character(new()) { Id = current.Id, Name = current.Name };
                AttachConnection(replacement);
                current.Connection.ActiveChar = replacement;
                _sessions[id] = replacement;
            }
            return current;
        });
        return new ExpeditionRecruitmentService(new MySqlExpeditionRecruitmentRepository(fixture), manager,
            world.Object, fixture, coordinator, TimeProvider.System);
    }

    private (ExpeditionManager Manager, Mock<IWorldManager> World) RealManager(Character actor,
        Func<Character> liveCandidate)
    {
        var world = new Mock<IWorldManager>();
        world.Setup(x => x.GetCharacterById(It.IsAny<uint>()))
            .Returns((uint id) => id == actor.Id ? actor : liveCandidate());
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object, new Mock<ITeamManager>().Object,
            world.Object, new Mock<IChatManager>().Object, fixture, new Mock<IItemManager>().Object, Factions());
        var expeditions = new Dictionary<FactionsEnum, Expedition> { [actor.Expedition.Id] = actor.Expedition };
        typeof(ExpeditionManager).GetField("_expeditions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, expeditions);
        return (manager, world);
    }

    private static IFactionManager Factions()
    {
        var factions = new Mock<IFactionManager>();
        factions.Setup(manager => manager.GetFaction(FactionsEnum.NuiaAlliance)).Returns(
            new AAEmu.Game.Models.Game.Faction.SystemFaction
                { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.Invalid });
        factions.Setup(manager => manager.GetFaction((FactionsEnum)101)).Returns(
            new AAEmu.Game.Models.Game.Faction.SystemFaction
                { Id = (FactionsEnum)101, MotherId = FactionsEnum.NuiaAlliance });
        return factions.Object;
    }

    private Character Actor(int guildId, uint? characterId = null)
    {
        var actor = new Character(new())
        {
            Id = characterId ?? (uint)(100 + guildId),
            Name = $"Officer{guildId}"
        };
        var guild = _expeditions.GetValueOrDefault((FactionsEnum)guildId) ?? CreateExpedition(guildId);
        _expeditions[guild.Id] = guild;
        guild.OwnerId = actor.Id;
        guild.OwnerName = actor.Name;
        actor.Expedition = guild;
        guild.Members.Add(new ExpeditionMember { ExpeditionId = guild.Id, CharacterId = actor.Id, Role = 1 });
        AttachConnection(actor);
        _sessions[actor.Id] = actor;
        return actor;
    }

    private Character CurrentCharacter(uint id)
    {
        var character = new Character(new()) { Id = id, Name = "Applicant" };
        AttachConnection(character);
        _sessions[id] = character;
        return character;
    }

    private static void AttachConnection(Character character)
    {
        character.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = character };
    }

    private static Expedition CreateExpedition(int guildId)
    {
        return new Expedition
        {
            Id = (FactionsEnum)guildId,
            MotherId = FactionsEnum.NuiaAlliance,
            Policies = [new ExpeditionRolePolicy { ExpeditionId = (FactionsEnum)guildId, Role = 1, Invite = true }],
            Members = []
        };
    }

    private void Seed(params int[] guildIds)
    {
        _sessions.Clear();
        _expeditions.Clear();
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM expedition_recruitment_applications; DELETE FROM expedition_recruitments; DELETE FROM characters; DELETE FROM expeditions";
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO characters
                (id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id,family,money,expedition_rejoin_until)
            VALUES(10,1,'Applicant',55,0,1,2,3,4,0,0,0,0)
            """;
        command.ExecuteNonQuery();
        foreach (var guildId in guildIds)
        {
            _expeditions[(FactionsEnum)guildId] = CreateExpedition(guildId);
            command.CommandText = """
                INSERT INTO expeditions (id) VALUES(@guild);
                INSERT INTO expedition_recruitments
                    (expedition_id,interest_mask,introduction,registered_at,expires_at)
                VALUES(@guild,1,'open',@now,@expires);
                INSERT INTO expedition_recruitment_applications
                    (expedition_id,character_id,memo,registered_at)
                VALUES(@guild,10,'memo',@now)
                """;
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@guild", guildId);
            command.Parameters.AddWithValue("@now", Now);
            command.Parameters.AddWithValue("@expires", Now.AddDays(3));
            command.ExecuteNonQuery();
        }
    }

    private long Scalar(string sql)
    {
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class ControlledJoinCoordinator : IExpeditionRecruitmentJoinCoordinator
    {
        public bool FailAfterMembershipWrite { get; init; }
        public bool OnlinePathUsed { get; private set; }
        public ControlledJoin LastTransition { get; private set; }

        public bool TryBegin(Character actor, Character candidate, Expedition expedition,
            out IExpeditionRecruitmentJoin transition)
        {
            OnlinePathUsed = true;
            return Begin(expedition, candidate.Id, out transition);
        }

        public bool TryBegin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition,
            out IExpeditionRecruitmentJoin transition) => Begin(expedition, candidate.CharacterId, out transition);

        private bool Begin(Expedition expedition, uint characterId, out IExpeditionRecruitmentJoin transition)
        {
            LastTransition = new ControlledJoin((uint)expedition.Id, characterId, FailAfterMembershipWrite);
            transition = LastTransition;
            return true;
        }
    }

    private sealed class ControlledJoin(uint expeditionId, uint characterId, bool fail) : IExpeditionRecruitmentJoin
    {
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }

        public void Persist(MySqlConnection connection, MySqlTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE characters SET expedition_id=@guild WHERE id=@character AND expedition_id=0";
            command.Parameters.AddWithValue("@guild", expeditionId);
            command.Parameters.AddWithValue("@character", characterId);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Membership was claimed elsewhere.");
            if (fail) throw new InvalidOperationException("Controlled persistence failure.");
        }

        public void Commit() => Committed = true;
        public void Dispose() => Disposed = true;
    }
}
