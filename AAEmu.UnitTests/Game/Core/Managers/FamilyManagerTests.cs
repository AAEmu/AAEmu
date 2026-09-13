using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Families;
using AAEmu.Game.Models.Game.Units;

using System.Reflection;
using AAEmu.Commons.Network.Core;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class FamilyManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamilyId = Mock.Of<IFamilyIdManager>();
        var manager = new FamilyManager(mockWorld.Object, mockChat.Object, mockFamilyId.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockChat);
        Mock.VerifyNoOtherCalls(mockFamilyId);
    }

    [Test]
    public async Task OnCharacterLogout_IsIdempotentAndIgnoresStaleSession()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        var mockChat = Mock.Of<IChatManager>();
        var mockFamilyId = Mock.Of<IFamilyIdManager>();
        var manager = new FamilyManager(mockWorld.Object, mockChat.Object, mockFamilyId.Object);
        var current = new Character(new UnitCustomModelParams()) { Id = 7, Family = 12 };
        var stale = new Character(new UnitCustomModelParams()) { Id = 7, Family = 12 };
        var member = new FamilyMember { Id = current.Id, Character = current, Name = "Member", Title = "" };
        var family = new Family { Id = current.Family };
        family.AddMember(member);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", new Dictionary<uint, FamilyMember> { [member.Id] = member });

        manager.OnCharacterLogout(stale);
        await Assert.That(member.Character).IsSameReferenceAs(current);

        manager.OnCharacterLogout(current);
        manager.OnCharacterLogout(current);
        await Assert.That(member.Character).IsNull();
    }

    [Test]
    public async Task OnCharacterRefresh_UpdatesOnlyTheAuthoritativeLiveMember()
    {
        var character = NewCharacter(7, 12, "Member");
        character.Level = 55;
        var member = new FamilyMember { Id = character.Id, Character = character, Name = character.Name, Title = "", Level = 1, HeirLevel = 8 };
        var family = new Family { Id = character.Family };
        family.AddMember(member);
        var manager = NewManager(Mock.Of<IWorldManager>(), _ => { }, family);

        manager.OnCharacterRefresh(character);

        await Assert.That(member.Level).IsEqualTo((byte)55);
        await Assert.That(member.HeirLevel).IsEqualTo(character.HeirLevel);
    }

    [Test]
    public async Task FamilyChat_RejectsAStaleCharacterFamilyId()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var stale = NewCharacter(2, 10, "Stale");
        var manager = NewManager(Mock.Of<IWorldManager>(), _ => { }, NewFamily(owner));

        var sent = manager.SendChatMessage(stale, "hello", 0, 0);

        await Assert.That(sent).IsFalse();
        await Assert.That(manager.SendChatMessage(owner, "hello", 0, 0)).IsTrue();
    }

    [Test]
    public async Task ClientMutations_RejectCharacterFromReplacedSession()
    {
        var current = NewCharacter(1, 10, "Owner");
        var stale = NewCharacter(1, 10, "Owner");
        var member = NewCharacter(2, 10, "Member");
        var family = NewFamily(current, member);
        family.Notice = "Original";
        var saves = 0;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(current.Id).Returns(current);
        current.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = current };
        stale.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = current };
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, Mock.Of<IFamilyPurchaseService>().Object,
            _ => saves++);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", family.Members.ToDictionary(x => x.Id));

        manager.SetNotice(stale, "Stale update");
        manager.ChangeMemberRole(stale, member.Id, 2);
        manager.ChangeTitle(stale, member.Id, "Changed");
        manager.ChangeOwner(stale, member.Id);
        manager.KickMember(stale, member.Id);
        manager.LeaveFamily(stale);
        manager.InviteToFamily(stale, "Nobody", "Child");
        var sentChat = manager.SendChatMessage(stale, "stale", 0, 0);
        var leveled = manager.TryLevelUp(stale, 2);

        await Assert.That(family.Notice).IsEqualTo("Original");
        await Assert.That(family.Members.Count).IsEqualTo(2);
        await Assert.That(family.GetMember(member)?.Title).IsEqualTo("");
        await Assert.That(family.GetMember(member)?.Role).IsEqualTo((byte)0);
        await Assert.That(current.Family).IsEqualTo(family.Id);
        await Assert.That(saves).IsEqualTo(0);
        await Assert.That(sentChat).IsFalse();
        await Assert.That(leveled).IsFalse();
        world.GetCharacter(Any<string>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ReplyToInvite_RejectsInviteeFromReplacedSession()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var currentInvitee = NewCharacter(2, 0, "Invitee");
        var staleInvitee = NewCharacter(2, 0, "Invitee");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(currentInvitee.Name).Returns(currentInvitee);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        world.GetCharacterById(currentInvitee.Id).Returns(currentInvitee);
        inviter.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = inviter };
        currentInvitee.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = currentInvitee };
        staleInvitee.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = currentInvitee };
        var purchases = Mock.Of<IFamilyPurchaseService>();
        purchases.ConsumeInvitation(inviter)
            .Returns(new FamilyPurchaseResult(true, FamilyPurchaseFailure.None));
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { });

        manager.InviteToFamily(inviter, currentInvitee.Name, "Child");
        manager.ReplyToInvite(inviter.Id, staleInvitee, true, "Child");

        var pending = (System.Collections.IDictionary)typeof(FamilyManager)
            .GetField("_pendingInvitations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        await Assert.That(pending.Count).IsEqualTo(1);
        await Assert.That(currentInvitee.Family).IsEqualTo(0u);
    }

    [Test]
    public async Task InviteToFamily_RejectsReplacedInviteeBeforeCertificatePurchase()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var currentInvitee = NewCharacter(2, 0, "Invitee");
        var staleInvitee = NewCharacter(2, 0, "Invitee");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(staleInvitee.Name).Returns(staleInvitee);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        world.GetCharacterById(staleInvitee.Id).Returns(currentInvitee);
        inviter.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = inviter };
        currentInvitee.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = currentInvitee };
        staleInvitee.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = currentInvitee };
        var purchases = Mock.Of<IFamilyPurchaseService>();
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { });

        manager.InviteToFamily(inviter, staleInvitee.Name, "Child");

        purchases.ConsumeInvitation(Any<Character>()).WasCalled(Times.Never);
        var pending = (System.Collections.IDictionary)typeof(FamilyManager)
            .GetField("_pendingInvitations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FamilyRemoveMember_UnknownMemberDoesNotMutateFamily()
    {
        var family = new Family { Id = 12 };
        var member = new FamilyMember { Id = 7, Name = "Member", Title = "" };
        family.AddMember(member);

        var removed = family.RemoveMember(new FamilyMember { Id = 8, Name = "Other", Title = "" });

        await Assert.That(removed).IsFalse();
        await Assert.That(family.Members).HasSingleItem();
        await Assert.That(family.Members[0]).IsSameReferenceAs(member);
    }

    [Test]
    public async Task ReplyToInvite_SpoofDoesNotConsumeRealInvitation()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        var saved = new List<Family>();
        var manager = NewManager(world, saved.Add);

        manager.InviteToFamily(inviter, invited.Name, "Child");
        manager.ReplyToInvite(99, invited, true, "Forged");
        manager.ReplyToInvite(inviter.Id, invited, true, "Ignored");
        manager.ReplyToInvite(inviter.Id, invited, true, "Replay");

        await Assert.That(saved).HasSingleItem();
        await Assert.That(inviter.Family).IsEqualTo(100u);
        await Assert.That(invited.Family).IsEqualTo(100u);
        await Assert.That(saved[0].Members.Single(x => x.Id == invited.Id).Title).IsEqualTo("Child");
    }

    [Test]
    public async Task ReplyToInvite_RejectsInviterWhoMovedFamily()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        var saved = new List<Family>();
        var manager = NewManager(world, saved.Add);
        manager.InviteToFamily(inviter, invited.Name, "Child");

        inviter.Family = 50;
        manager.ReplyToInvite(inviter.Id, invited, true, "Child");

        await Assert.That(saved).IsEmpty();
        await Assert.That(invited.Family).IsEqualTo(0u);
    }

    [Test]
    public async Task PendingInviteRetryAndDecline_DoNotChargeAgain()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        var purchases = Mock.Of<IFamilyPurchaseService>();
        purchases.ConsumeInvitation(inviter)
            .Returns(new FamilyPurchaseResult(true, FamilyPurchaseFailure.None));
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { }, isCurrentSession: _ => true);

        manager.InviteToFamily(inviter, invited.Name, "Child");
        manager.InviteToFamily(inviter, invited.Name, "Child");
        manager.ReplyToInvite(inviter.Id, invited, false, "Ignored");

        purchases.ConsumeInvitation(inviter).WasCalled(Times.Once);
        await Assert.That(invited.Family).IsEqualTo(0u);
    }

    [Test]
    public async Task PurchaseCallbacks_RunAfterFamilyMutationLockIsReleased()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        var purchases = Mock.Of<IFamilyPurchaseService>();
        FamilyManager manager = null;
        Task callbackWorker = null;
        purchases.ConsumeInvitation(inviter).Returns(new FamilyPurchaseResult(
            true,
            FamilyPurchaseFailure.None,
            () =>
            {
                callbackWorker = Task.Run(() => manager.GetFamilyOfCharacter(uint.MaxValue));
                if (!callbackWorker.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Family purchase callback could not reacquire family state.");
            }));
        manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { }, isCurrentSession: _ => true);

        try
        {
            manager.InviteToFamily(inviter, invited.Name, "Child");
            await Assert.That(callbackWorker is not null).IsTrue();
            await Assert.That(callbackWorker!.IsCompletedSuccessfully).IsTrue();
        }
        finally
        {
            if (callbackWorker != null)
                await callbackWorker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task InviteDuringConfiguredRejoinDelay_DoesNotSpendCertificate()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        invited.FamilyRejoinUntil = 200;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        var purchases = Mock.Of<IFamilyPurchaseService>();
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { }, () => 100, _ => true);

        manager.InviteToFamily(inviter, invited.Name, "Child");

        purchases.ConsumeInvitation(Any<Character>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ReplyToInvite_RechecksRejoinDelayBeforeCreatingFamily()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        var saved = new List<Family>();
        var manager = NewManager(world, saved.Add);

        manager.InviteToFamily(inviter, invited.Name, "Child");
        invited.FamilyRejoinUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1000;
        manager.ReplyToInvite(inviter.Id, invited, true, "Child");

        await Assert.That(saved).IsEmpty();
        await Assert.That(invited.Family).IsEqualTo(0u);
    }

    [Test]
    public async Task CreateFamily_PersistenceFailureRollsBackPublishedMembership()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        world.GetCharacterById(inviter.Id).Returns(inviter);
        var manager = NewManager(world, _ => throw new InvalidOperationException("save failed"));
        manager.InviteToFamily(inviter, invited.Name, "Child");

        await Assert.That(() => manager.ReplyToInvite(inviter.Id, invited, true, "Child"))
            .Throws<InvalidOperationException>();
        await Assert.That(inviter.Family).IsEqualTo(0u);
        await Assert.That(invited.Family).IsEqualTo(0u);
        await Assert.That(() => manager.GetFamily(100)).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task OwnerActions_RejectForeignTargetsAndSelfKick()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var local = NewCharacter(2, 10, "Local");
        var foreign = NewCharacter(3, 20, "Foreign");
        var family = NewFamily(owner, local);
        var other = NewFamily(foreign);
        var saved = new List<Family>();
        var manager = NewManager(Mock.Of<IWorldManager>(), saved.Add, family, other);

        manager.ChangeTitle(owner, foreign.Id, "Changed");
        manager.ChangeOwner(owner, foreign.Id);
        manager.KickMember(owner, owner.Id);

        await Assert.That(saved).IsEmpty();
        await Assert.That(family.Members).Count().IsEqualTo(2);
        await Assert.That(other.Members[0].Role).IsEqualTo((byte)1);
    }

    [Test]
    public async Task OwnerCannotLeaveThreeMemberFamily()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var family = NewFamily(owner, NewCharacter(2, 10, "Two"), NewCharacter(3, 10, "Three"));
        var saved = new List<Family>();
        var manager = NewManager(Mock.Of<IWorldManager>(), saved.Add, family);

        manager.LeaveFamily(owner);

        await Assert.That(saved).IsEmpty();
        await Assert.That(owner.Family).IsEqualTo(10u);
        await Assert.That(family.Members).Count().IsEqualTo(3);
    }

    [Test]
    public async Task TwoMemberDisband_PersistenceFailureRestoresBothMembers()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var member = NewCharacter(2, 10, "Member");
        var family = NewFamily(owner, member);
        var manager = NewManager(Mock.Of<IWorldManager>(),
            _ => throw new InvalidOperationException("save failed"), family);

        await Assert.That(() => manager.LeaveFamily(member)).Throws<InvalidOperationException>();

        await Assert.That(owner.Family).IsEqualTo(10u);
        await Assert.That(member.Family).IsEqualTo(10u);
        await Assert.That(family.Members).Count().IsEqualTo(2);
        await Assert.That(manager.GetFamily(10)).IsSameReferenceAs(family);
    }

    [Test]
    public async Task TwoMemberDisband_AppliesRejoinDelayToEveryOnlineMember()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var member = NewCharacter(2, 10, "Member");
        var family = NewFamily(owner, member);
        var manager = NewManager(Mock.Of<IWorldManager>(), _ => { }, family);

        manager.LeaveFamily(member);

        await Assert.That(owner.Family).IsEqualTo(0u);
        await Assert.That(member.Family).IsEqualTo(0u);
        await Assert.That(owner.FamilyRejoinUntil).IsEqualTo(member.FamilyRejoinUntil);
        await Assert.That(owner.FamilyRejoinUntil).IsGreaterThan(0L);
    }

    [Test]
    public async Task DepartureLoss_UsesConfiguredPercentAndRetainsPurchasedLevel()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var leaving = NewCharacter(2, 10, "Leaving");
        var family = NewFamily(owner, leaving, NewCharacter(3, 10, "Three"), NewCharacter(4, 10, "Four"));
        family.Level = 2;
        family.Exp = 99;
        var saves = 0;
        var manager = NewManager(Mock.Of<IWorldManager>(), _ => saves++, family);

        manager.LeaveFamily(leaving);

        await Assert.That(family.Exp).IsEqualTo(90u);
        await Assert.That(family.Level).IsEqualTo(2u);
        await Assert.That(saves).IsEqualTo(1);
    }

    [Test]
    public async Task LoginExperience_IsGrantedOncePerUtcDayAndPersisted()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.LoginExpKey, 10);
        var character = NewCharacter(1, 10, "Owner");
        var family = NewFamily(character);
        family.Exp = 5;
        family.Members[0].LoginRewardTime = 1;
        var saves = 0;
        var manager = new FamilyManager(Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, Mock.Of<IFamilyPurchaseService>().Object,
            _ => saves++, () => 86400);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", family.Members.ToDictionary(x => x.Id));

        manager.OnCharacterLogin(character);
        manager.OnCharacterLogin(character);

        await Assert.That(family.Exp).IsEqualTo(15u);
        await Assert.That(family.Members[0].LoginRewardTime).IsEqualTo(86400L);
        await Assert.That(saves).IsEqualTo(1);
    }

    [Test]
    public async Task RenameDuringCooldown_DoesNotSpendRenameItem()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.NameChangeDelayDaysKey, 7);
        var owner = NewCharacter(1, 10, "Owner");
        var family = NewFamily(owner);
        family.Name = "Old Name";
        family.ChangeNameTime = 100;
        var purchases = Mock.Of<IFamilyPurchaseService>();
        var manager = new FamilyManager(Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => { },
            () => 100 + 7 * 86400 - 1, _ => true);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", family.Members.ToDictionary(x => x.Id));

        manager.SetName(owner, "New Name");

        purchases.Rename(Any<Character>(), Any<Family>(), Any<string>(), Any<long>()).WasCalled(Times.Never);
        await Assert.That(family.Name).IsEqualTo("Old Name");
    }

    [Test]
    public async Task RenameToCurrentName_DoesNotSpendOrPersist()
    {
        var owner = NewCharacter(1, 10, "Owner");
        var family = NewFamily(owner);
        family.Name = "Same Name";
        var purchases = Mock.Of<IFamilyPurchaseService>();
        var saves = 0;
        var manager = new FamilyManager(Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => saves++, isCurrentSession: _ => true);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", family.Members.ToDictionary(x => x.Id));

        manager.SetName(owner, "Same Name");

        purchases.Rename(Any<Character>(), Any<Family>(), Any<string>(), Any<long>()).WasCalled(Times.Never);
        await Assert.That(saves).IsEqualTo(0);
    }

    [Test]
    public async Task FullFamilyDoesNotReserveInvitation()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.MaximumCountKey, 8);
        var members = Enumerable.Range(1, FamilyContentConfig.MaximumCount)
            .Select(i => NewCharacter((uint)i, 10, $"Member{i}")).ToArray();
        var invited = NewCharacter(20, 0, "Invited");
        var family = NewFamily(members);
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(invited);
        var manager = NewManager(world, _ => { }, family);

        manager.InviteToFamily(members[0], invited.Name, "Child");

        var pending = (System.Collections.IDictionary)typeof(FamilyManager)
            .GetField("_pendingInvitations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        await Assert.That(pending.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Invite_AcquiresPersistenceGateBeforeFamilyOperation()
    {
        var inviter = NewCharacter(1, 0, "Inviter");
        var invited = NewCharacter(2, 0, "Invited");
        var observedGate = false;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacter(invited.Name).Returns(() =>
        {
            observedGate = PersistenceGate.IsOperationHeld;
            return invited;
        });
        var manager = NewManager(world, _ => { });

        manager.InviteToFamily(inviter, invited.Name, "Child");

        await Assert.That(observedGate).IsTrue();
    }

    private static Character NewCharacter(uint id, uint family, string name) =>
        new(new UnitCustomModelParams()) { Id = id, Family = family, Name = name };

    private static Family NewFamily(params Character[] characters)
    {
        var family = new Family { Id = characters[0].Family };
        foreach (var character in characters)
            family.AddMember(new FamilyMember { Id = character.Id, Character = character, Name = character.Name,
                Role = (byte)(family.Members.Count == 0 ? 1 : 0), Title = "" });
        return family;
    }

    private static FamilyManager NewManager(Mock<IWorldManager> world, Action<Family> save, params Family[] families)
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.MaximumCountKey, 8);
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.LeaveExpPercentKey, 10);
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.RejoinDelayHoursKey, 24);
        var ids = Mock.Of<IFamilyIdManager>();
        ids.GetNextId().Returns(100);
        var purchases = Mock.Of<IFamilyPurchaseService>();
        purchases.ConsumeInvitation(Any<Character>())
            .Returns(new FamilyPurchaseResult(true, FamilyPurchaseFailure.None));
        var manager = new FamilyManager(world.Object, Mock.Of<IChatManager>().Object, ids.Object,
            purchases.Object, save, isCurrentSession: _ => true);
        SetField(manager, "_families", families.ToDictionary(x => x.Id));
        SetField(manager, "_familyMembers", families.SelectMany(x => x.Members).ToDictionary(x => x.Id));
        return manager;
    }

    private static void SetField<T>(FamilyManager manager, string name, T value)
    {
        typeof(FamilyManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, value);
    }
}
