using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ExpeditionManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockExpId = Mock.Of<IExpeditionIdManager>();
        var mockTeam = Mock.Of<ITeamManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var manager = new ExpeditionManager(mockExpId.Object, mockTeam.Object, mockWorld.Object, mockChat.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockExpId);
        Mock.VerifyNoOtherCalls(mockTeam);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
    }

    [Test]
    public async Task InvitationStore_ConsumesOnlyMatchingInvitationOnce()
    {
        var store = new ExpeditionInvitationStore();
        var inviterSession = new object();
        var candidateSession = new object();
        var invitation = new ExpeditionInvitation(FactionsEnum.NuiaAlliance, 42, FactionsEnum.NuiaAlliance, inviterSession, candidateSession);
        store.Set(7, invitation);

        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.HaranyaAlliance, 42, out _)).IsFalse();
        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.NuiaAlliance, 99, out _)).IsFalse();
        await Assert.That(store.TryConsume(7, new object(), FactionsEnum.NuiaAlliance, 42, out _)).IsFalse();
        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.NuiaAlliance, 42, out var consumed)).IsTrue();
        await Assert.That(consumed).IsEqualTo(invitation);
        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.NuiaAlliance, 42, out _)).IsFalse();
    }

    [Test]
    public async Task InvitationStore_ReplacesOlderInvitationForSameCandidate()
    {
        var store = new ExpeditionInvitationStore();
        var firstCandidateSession = new object();
        var replacementCandidateSession = new object();
        store.Set(7, new ExpeditionInvitation(FactionsEnum.NuiaAlliance, 42, FactionsEnum.NuiaAlliance, new object(), firstCandidateSession));
        var replacement = new ExpeditionInvitation(FactionsEnum.HaranyaAlliance, 99, FactionsEnum.HaranyaAlliance, new object(), replacementCandidateSession);
        store.Set(7, replacement);

        await Assert.That(store.TryConsume(7, firstCandidateSession, FactionsEnum.NuiaAlliance, 42, out _)).IsFalse();
        await Assert.That(store.TryConsume(7, replacementCandidateSession, FactionsEnum.HaranyaAlliance, 99, out var consumed)).IsTrue();
        await Assert.That(consumed).IsEqualTo(replacement);
    }

    [Test]
    public async Task InvitationStore_LogoutClearsOnlyMatchingSessionInvitations()
    {
        var store = new ExpeditionInvitationStore();
        var inviterSession = new object();
        var candidateSession = new object();
        store.Set(7, new ExpeditionInvitation(FactionsEnum.NuiaAlliance, 42, FactionsEnum.NuiaAlliance, inviterSession, candidateSession));

        store.RemoveFor(7, new object());
        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.NuiaAlliance, 42, out _)).IsTrue();

        store.Set(7, new ExpeditionInvitation(FactionsEnum.NuiaAlliance, 42, FactionsEnum.NuiaAlliance, inviterSession, candidateSession));
        store.RemoveFor(42, inviterSession);
        await Assert.That(store.TryConsume(7, candidateSession, FactionsEnum.NuiaAlliance, 42, out _)).IsFalse();
    }

    [Test]
    public async Task PersistenceOperationScope_DoesNotReenterOuterOperation()
    {
        bool ownsGate;
        bool outerStillHeld;
        PersistenceGate.EnterOperation();
        try
        {
            using (var scope = PersistenceOperationScope.Enter())
                ownsGate = scope.OwnsGate;
            outerStillHeld = PersistenceGate.IsOperationHeld;
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
        await Assert.That(ownsGate).IsFalse();
        await Assert.That(outerStillHeld).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task PersistenceOperationScope_WaitsForSaveAndThenCompletes()
    {
        var guildLock = new object();
        using var saveEntered = new ManualResetEventSlim();
        using var releaseSave = new ManualResetEventSlim();
        var saveTask = Task.Factory.StartNew(() =>
        {
            PersistenceGate.EnterSave();
            try
            {
                saveEntered.Set();
                releaseSave.Wait();
            }
            finally
            {
                PersistenceGate.ExitSave();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<bool> operationTask = null;
        try
        {
            await Assert.That(saveEntered.Wait(TimeSpan.FromSeconds(2))).IsTrue();
            operationTask = Task.Factory.StartNew(() =>
            {
                using var scope = PersistenceOperationScope.Enter();
                lock (guildLock)
                    return scope.OwnsGate;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await Task.Delay(50);
            await Assert.That(operationTask.IsCompleted).IsFalse();
        }
        finally
        {
            releaseSave.Set();
            await saveTask.WaitAsync(TimeSpan.FromSeconds(2));
            if (operationTask != null)
                await operationTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        await Assert.That(await operationTask.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();
    }

    [Test]
    public async Task GuildLevelChangeRules_RequireOwnerExactNextLevelAndEnoughExp()
    {
        var target = new ExpeditionLevel { Id = 3, TotalExp = 500 };

        await Assert.That(GuildLevelChangeRules.CanApply(10, 10, byte.MaxValue, 2, 3, 500, target)).IsTrue();
        await Assert.That(GuildLevelChangeRules.CanApply(10, 11, byte.MaxValue, 2, 3, 500, target)).IsFalse();
        await Assert.That(GuildLevelChangeRules.CanApply(10, 10, 3, 2, 3, 500, target)).IsFalse();
        await Assert.That(GuildLevelChangeRules.CanApply(10, 10, byte.MaxValue, 2, 4, 500, target)).IsFalse();
        await Assert.That(GuildLevelChangeRules.CanApply(10, 10, byte.MaxValue, 2, 3, 499, target)).IsFalse();
    }

    [Test]
    public async Task ExpeditionList_UsesNativeTwentyEntryChunksAndPreservesOrder()
    {
        var expeditions = Enumerable.Range(1, 41)
            .Select(id => new Expedition { Id = (FactionsEnum)(uint)id })
            .ToArray();

        var chunks = ExpeditionManager.ChunkExpeditionList(expeditions);

        await Assert.That(chunks.Count).IsEqualTo(3);
        await Assert.That(chunks[0].Length).IsEqualTo(20);
        await Assert.That(chunks[1].Length).IsEqualTo(20);
        await Assert.That(chunks[2].Length).IsEqualTo(1);
        await Assert.That(chunks.SelectMany(x => x).SequenceEqual(expeditions)).IsTrue();
        await Assert.That(ExpeditionManager.ChunkExpeditionList([]).Single()).IsEmpty();
    }

    [Test]
    public async Task GuildExpProgressionRules_ClampAtDailyLimit()
    {
        await Assert.That(GuildExpProgressionRules.GetAcceptedAmount(100, 450, 500)).IsEqualTo(50u);
        await Assert.That(GuildExpProgressionRules.GetAcceptedAmount(100, 500, 500)).IsEqualTo(0u);
        await Assert.That(GuildExpProgressionRules.GetAcceptedAmount(100, 0, 0)).IsEqualTo(100u);
    }

    [Test]
    public async Task MemberJoin_DisposeRollsBackStagedCharacterAndRoster()
    {
        var expedition = new Expedition { Id = FactionsEnum.NuiaAlliance };
        var candidate = new Character(new UnitCustomModelParams()) { Id = 77 };
        var member = new ExpeditionMember { CharacterId = candidate.Id, ExpeditionId = expedition.Id };
        Monitor.Enter(expedition.SyncRoot);
        var membershipSync = new object();
        Monitor.Enter(membershipSync);
        var transition = new ExpeditionManager.ExpeditionMemberJoin(candidate, expedition, member, membershipSync, false,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var stagedCharacter = ReferenceEquals(candidate.Expedition, expedition);
        var stagedRoster = expedition.Members.Contains(member);
        transition.Dispose();

        await Assert.That(stagedCharacter).IsTrue();
        await Assert.That(stagedRoster).IsTrue();
        await Assert.That(candidate.Expedition).IsNull();
        await Assert.That(expedition.Members.Contains(member)).IsFalse();
    }

    [Test]
    public async Task MemberJoin_CompetingGuildsSerializeCandidateStagingAndRollback()
    {
        var alliance = new SystemFaction { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.NuiaAlliance };
        var actor1 = new Character(new UnitCustomModelParams()) { Id = 1, Faction = alliance };
        var actor2 = new Character(new UnitCustomModelParams()) { Id = 2, Faction = alliance };
        var candidate = new Character(new UnitCustomModelParams()) { Id = 3, Faction = alliance };
        actor1.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = actor1 };
        actor2.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = actor2 };
        candidate.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = candidate };
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(actor1.Id).Returns(actor1);
        world.GetCharacterById(actor2.Id).Returns(actor2);
        world.GetCharacterById(candidate.Id).Returns(candidate);
        var factions = Mock.Of<IFactionManager>();
        factions.GetFaction(FactionsEnum.NuiaAlliance).Returns(
            new SystemFaction { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.Invalid });
        var manager = new ExpeditionManager(Mock.Of<IExpeditionIdManager>().Object, Mock.Of<ITeamManager>().Object,
            world.Object, Mock.Of<IChatManager>().Object, Mock.Of<IExpeditionPersistenceConnectionFactory>().Object,
            Mock.Of<IItemManager>().Object, factions.Object);
        var guild1 = MakeInvitingGuild(FactionsEnum.NuiaAlliance, actor1);
        var guild2 = MakeInvitingGuild((FactionsEnum)101, actor2);
        using var firstStaged = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var secondStaged = new ManualResetEventSlim();
        var firstBegan = false;
        Exception firstFailure = null;

        var first = Task.Factory.StartNew(() =>
        {
            ExpeditionManager.ExpeditionMemberJoin transition = null;
            try
            {
                try
                {
                    var began = manager.TryBeginMemberJoin(actor1, candidate, guild1, out transition);
                    firstBegan = began;
                    firstStaged.Set();
                    if (began)
                        releaseFirst.Wait();
                    return began;
                }
                catch (Exception exception)
                {
                    firstFailure = exception;
                    firstStaged.Set();
                    throw;
                }
            }
            finally
            {
                transition?.Dispose();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<(bool began, bool stagedInOnlySecond)> second = null;
        try
        {
            await Assert.That(firstStaged.Wait(TimeSpan.FromSeconds(2))).IsTrue();
            await Assert.That(firstFailure).IsNull();
            await Assert.That(firstBegan).IsTrue();
            second = Task.Factory.StartNew(() =>
            {
                ExpeditionManager.ExpeditionMemberJoin transition = null;
                try
                {
                    secondEntered.Set();
                    var began = manager.TryBeginMemberJoin(actor2, candidate, guild2, out transition);
                    secondStaged.Set();
                    var stagedInOnlySecond = candidate.Expedition == guild2 &&
                                             !guild1.Members.Any(x => x.CharacterId == candidate.Id) &&
                                             guild2.Members.Count(x => x.CharacterId == candidate.Id) == 1;
                    return (began, stagedInOnlySecond);
                }
                finally
                {
                    transition?.Dispose();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            await Assert.That(secondEntered.Wait(TimeSpan.FromSeconds(2))).IsTrue();
            await Task.Delay(50);
            await Assert.That(secondStaged.IsSet).IsFalse();
            releaseFirst.Set();
            await Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();
            var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(secondResult.began).IsTrue();
            await Assert.That(secondResult.stagedInOnlySecond).IsTrue();
            await Assert.That(candidate.Expedition).IsNull();
        }
        finally
        {
            releaseFirst.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (second != null)
                await second.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static Expedition MakeInvitingGuild(FactionsEnum id, Character actor)
    {
        var guild = new Expedition
        {
            Id = id,
            MotherId = FactionsEnum.NuiaAlliance,
            Level = 1,
            Members = [],
            Policies = [new ExpeditionRolePolicy { ExpeditionId = id, Role = 1, Invite = true }]
        };
        actor.Expedition = guild;
        guild.Members.Add(new ExpeditionMember
        {
            ExpeditionId = id,
            CharacterId = actor.Id,
            Role = 1,
            Name = actor.Name
        });
        return guild;
    }
}
