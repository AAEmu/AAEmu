using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ExpeditionSessionAuthorizationTests
{
    [Test]
    public async Task StaleOwnerSessionCannotMutateRosterPolicyOrMembership()
    {
        var currentOwner = Character(1, "Owner");
        var staleOwner = Character(1, "Owner");
        var target = Character(2, "Target");
        var staleTarget = Character(2, "Target");
        var expedition = Expedition(currentOwner, target);
        staleOwner.Expedition = expedition;
        staleTarget.Expedition = expedition;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(currentOwner.Id).Returns(currentOwner);
        world.GetCharacterById(target.Id).Returns(target);
        var staleConnection = Connect(staleOwner);
        Connect(staleTarget);
        Connect(currentOwner);
        var persistence = Mock.Of<IExpeditionPersistenceConnectionFactory>();
        var manager = Manager(world, persistence);
        var originalPolicy = expedition.GetPolicyByRole(0)!.Clone();

        manager.ChangeMemberRole(staleConnection, 1, target.Id);
        manager.ChangeOwner(staleConnection, target.Id);
        manager.ChangeExpeditionRolePolicy(staleConnection, new ExpeditionRolePolicy
        {
            ExpeditionId = expedition.Id,
            Role = 0,
            Name = "Changed",
            Invite = false,
            Promote = false
        });
        manager.Kick(staleConnection, target.Id);
        manager.LeaveCurrentSession(staleTarget);

        await Assert.That(expedition.OwnerId).IsEqualTo(currentOwner.Id);
        await Assert.That(expedition.GetMember(target.Id)?.Role).IsEqualTo((byte)0);
        await Assert.That(expedition.GetMember(target.Id)).IsNotNull();
        await Assert.That(staleTarget.Expedition).IsSameReferenceAs(expedition);
        await Assert.That(expedition.GetPolicyByRole(0)?.Name).IsEqualTo(originalPolicy.Name);
        persistence.Open().WasCalled(Times.Never);
    }

    [Test]
    public async Task StaleInviterCannotOverwritePendingInvitation()
    {
        var currentOwner = Character(1, "Owner");
        var staleOwner = Character(1, "Owner");
        var target = Character(2, "Target");
        var expedition = Expedition(currentOwner, target);
        staleOwner.Expedition = expedition;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(currentOwner.Id).Returns(currentOwner);
        var manager = Manager(world, Mock.Of<IExpeditionPersistenceConnectionFactory>());
        var store = (ExpeditionInvitationStore)typeof(ExpeditionManager)
            .GetField("_pendingInvitations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var candidateSession = new object();
        var original = new ExpeditionInvitation(expedition.Id, 77, expedition.Id, new object(), candidateSession);
        store.Set(target.Id, original);

        manager.Invite(Connect(staleOwner), target.Name);

        await Assert.That(store.TryConsume(target.Id, candidateSession, expedition.Id, 77, out var retained)).IsTrue();
        await Assert.That(retained).IsEqualTo(original);
        world.GetCharacter(target.Name).WasCalled(Times.Never);
    }

    [Test]
    public void StaleFounderCannotCreateExpedition()
    {
        var current = Character(1, "Founder");
        var stale = Character(1, "Founder");
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(current.Id).Returns(current);
        var ids = Mock.Of<IExpeditionIdManager>();
        var persistence = Mock.Of<IExpeditionPersistenceConnectionFactory>();
        var manager = new ExpeditionManager(ids.Object, Mock.Of<ITeamManager>().Object, world.Object,
            Mock.Of<IChatManager>().Object, persistence.Object, Mock.Of<IItemManager>().Object);

        manager.CreateExpedition("Rejected", FactionsEnum.NuiaAlliance, Connect(stale));

        ids.GetNextId().WasCalled(Times.Never);
        persistence.Open().WasCalled(Times.Never);
    }

    private static ExpeditionManager Manager(Mock<IWorldManager> world,
        Mock<IExpeditionPersistenceConnectionFactory> persistence) =>
        new(Mock.Of<IExpeditionIdManager>().Object, Mock.Of<ITeamManager>().Object, world.Object,
            Mock.Of<IChatManager>().Object, persistence.Object, Mock.Of<IItemManager>().Object);

    private static Character Character(uint id, string name) =>
        new(new UnitCustomModelParams()) { Id = id, Name = name };

    private static GameConnection Connect(Character character)
    {
        var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = character };
        character.Connection = connection;
        return connection;
    }

    private static Expedition Expedition(Character owner, Character target)
    {
        var expedition = new Expedition
        {
            Id = (FactionsEnum)500,
            Name = "Guild",
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            Members =
            [
                new ExpeditionMember { ExpeditionId = (FactionsEnum)500, CharacterId = owner.Id, Name = owner.Name, Role = byte.MaxValue },
                new ExpeditionMember { ExpeditionId = (FactionsEnum)500, CharacterId = target.Id, Name = target.Name, Role = 0 }
            ],
            Policies =
            [
                new ExpeditionRolePolicy { ExpeditionId = (FactionsEnum)500, Role = byte.MaxValue, Name = "Owner", Invite = true, Promote = true, Expel = true },
                new ExpeditionRolePolicy { ExpeditionId = (FactionsEnum)500, Role = 0, Name = "Member" },
                new ExpeditionRolePolicy { ExpeditionId = (FactionsEnum)500, Role = 1, Name = "Officer" }
            ]
        };
        owner.Expedition = expedition;
        target.Expedition = expedition;
        return expedition;
    }
}
