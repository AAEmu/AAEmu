using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Activities;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public sealed class ExpeditionActivityServiceTests
{
    [After(Test)]
    public void TearDown()
    {
        SingletonContainer.ServiceProvider = null;
        ResetSingleton<WorldManager>();
        ResetSingleton<SusManager>();
        ResetSingleton<SkillManager>();
    }

    [Test]
    public async Task TeleportToPortal_RejectsStaleExpeditionReferenceBeforeReadingPortal()
    {
        var repository = Mock.Of<IExpeditionActivityRepository>();
        var world = Mock.Of<IWorldManager>();
        var actor = CreateCharacter(42);
        var expedition = new Expedition { Id = (FactionsEnum)700, OwnerId = actor.Id };
        actor.Expedition = expedition;
        world.GetCharacterById(actor.Id).Returns(actor);
        var repositoryReads = 0;
        repository.GetPortals(Any<uint>()).Callback(() => repositoryReads++).Returns([]);
        var service = CreateService(repository.Object, world.Object);

        var result = service.TeleportToPortal(actor, 1);

        await Assert.That(result).IsFalse();
        await Assert.That(repositoryReads).IsEqualTo(0);
    }

    [Test]
    public async Task PortalAndHistoryReads_RejectCharacterMissingFromCurrentWorldRegistry()
    {
        var repository = Mock.Of<IExpeditionActivityRepository>();
        var portalReads = 0;
        var historyReads = 0;
        repository.GetPortals(Any<uint>()).Callback(() => portalReads++).Returns([]);
        repository.GetManagementHistories(Any<uint>(), Any<int>()).Callback(() => historyReads++).Returns([]);
        var world = Mock.Of<IWorldManager>();
        var actor = CreateCharacter(44);
        var expedition = new Expedition
        {
            Id = (FactionsEnum)703,
            OwnerId = actor.Id,
            Members = [new ExpeditionMember { CharacterId = actor.Id }]
        };
        actor.Expedition = expedition;
        var service = CreateService(repository.Object, world.Object);

        var portals = service.GetPortals(actor);
        service.SendHistories(actor, ExpeditionHistoryPage.Management);

        await Assert.That(portals).IsEmpty();
        await Assert.That(portalReads).IsEqualTo(0);
        await Assert.That(historyReads).IsEqualTo(0);
    }

    [Test]
    public async Task SavePortal_RejectsNamesOverNativeUtf8ByteLimitBeforePersistence()
    {
        var repository = Mock.Of<IExpeditionActivityRepository>();
        var actor = CreateCharacter(42);
        var expedition = new Expedition
        {
            Id = (FactionsEnum)700,
            OwnerId = actor.Id,
            Members = [new ExpeditionMember { CharacterId = actor.Id }]
        };
        actor.Expedition = expedition;
        var writes = 0;
        repository.TryAddPortal(Any<AAEmu.Game.Models.Game.Expeditions.Activities.ExpeditionPortalPoint>(), Any<int>())
            .Callback(() => writes++).Returns(true);
        var service = CreateService(repository.Object, Mock.Of<IWorldManager>().Object);

        var result = service.SavePortal(actor, new SkillObjectUnk2
        {
            Name = new string('\u00e9', ExpeditionActivityService.MaximumPortalNameLength)
        });

        await Assert.That(result).IsNull();
        await Assert.That(writes).IsEqualTo(0);
    }

    [Test]
    public async Task TeleportToPortal_PreservesStoredNonzeroRadianYaw()
    {
        const uint actorId = 43;
        var sourceTemplate = new WorldTemplate { Id = 6 };
        var destinationTemplate = new WorldTemplate { Id = 7 };
        using var sourceWorld = new WorldInstance(sourceTemplate, 1, true, 16);
        sourceWorld.MateManager = new MateManager(sourceWorld);
        sourceWorld.SlaveManager = new SlaveManager(sourceWorld);
        using var destinationWorld = new WorldInstance(destinationTemplate, 1, true, 17);
        var singletonWorld = new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        var worlds = (ConcurrentDictionary<uint, WorldInstance>)typeof(WorldManager)
            .GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(singletonWorld)!;
        worlds[sourceWorld.Id] = sourceWorld;
        worlds[destinationWorld.Id] = destinationWorld;
        var skillManager = new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object);
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(singletonWorld)
            .AddSingleton(new SusManager(singletonWorld))
            .AddSingleton(skillManager)
            .BuildServiceProvider();

        var actor = CreateCharacter(actorId);
        actor.ParentWorld = sourceWorld;
        var expedition = new Expedition
        {
            Id = (FactionsEnum)701,
            OwnerId = actor.Id,
            Members = [new ExpeditionMember { CharacterId = actor.Id }]
        };
        actor.Expedition = expedition;
        var repository = Mock.Of<IExpeditionActivityRepository>();
        repository.GetPortals((uint)expedition.Id).Returns([
            new AAEmu.Game.Models.Game.Expeditions.Activities.ExpeditionPortalPoint
            {
                Id = 9,
                ExpeditionId = (uint)expedition.Id,
                ZoneId = 0,
                X = 1,
                Y = 2,
                Z = 3,
                ZRot = MathF.PI / 2f
            }
        ]);
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(actor.Id).Returns(actor);
        world.GetWorldTemplateByZoneKey(0).Returns(destinationTemplate);
        world.GetWorlds().Returns([sourceWorld, destinationWorld]);
        var service = CreateService(repository.Object, world.Object);

        var result = service.TeleportToPortal(actor, 9);

        await Assert.That(result).IsTrue();
        await Assert.That(actor.Transform.World.Rotation.Z).IsEqualTo(MathF.PI / 2f);
    }

    [Test]
    public async Task RecordInstanceResult_PersistsOnlyCurrentDistinctExpeditionMembers()
    {
        var repository = Mock.Of<IExpeditionActivityRepository>();
        ExpeditionInstanceHistory persisted = null;
        repository.AddInstanceHistory(Any<uint>(), Any<ExpeditionInstanceHistory>())
            .Callback((uint _, ExpeditionInstanceHistory history) => persisted = history);
        var world = Mock.Of<IWorldManager>();
        var expeditionManager = new ExpeditionManager(
            Mock.Of<IExpeditionIdManager>().Object,
            Mock.Of<ITeamManager>().Object,
            world.Object,
            Mock.Of<IChatManager>().Object);
        var expedition = new Expedition
        {
            Id = (FactionsEnum)702,
            Members =
            [
                new ExpeditionMember { CharacterId = 101 },
                new ExpeditionMember { CharacterId = 102 }
            ]
        };
        typeof(ExpeditionManager).GetField("_expeditions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(expeditionManager, new Dictionary<FactionsEnum, Expedition>
            {
                [expedition.Id] = expedition
            });
        var service = new ExpeditionActivityService(repository.Object, world.Object, expeditionManager,
            Mock.Of<IItemManager>().Object, Mock.Of<IExpeditionActivityConnectionFactory>().Object,
            Mock.Of<IFactionManager>().Object);
        var recordedAt = new DateTime(2026, 9, 13, 16, 0, 0, DateTimeKind.Utc);
        repository.GetInstanceHistories((uint)expedition.Id,
                SCExpeditionInstanceHistoryInfoListPacket.MaximumHistories)
            .Returns([]);

        var result = service.RecordInstanceResult((uint)expedition.Id, 41, 69, 125,
            ExpeditionInstancePlayResult.Win,
            [
                new ExpeditionInstanceHistoryMember(0, 101, ExpeditionInstanceMemberStatus.Finished),
                new ExpeditionInstanceHistoryMember(0, 101, ExpeditionInstanceMemberStatus.Started),
                new ExpeditionInstanceHistoryMember(0, 999, ExpeditionInstanceMemberStatus.Finished),
                new ExpeditionInstanceHistoryMember(0, 102, ExpeditionInstanceMemberStatus.Started)
            ], recordedAt);

        await Assert.That(result).IsSameReferenceAs(persisted);
        await Assert.That(result.Members.Select(member => member.CharacterId)).IsEquivalentTo([101UL, 102UL]);
        await Assert.That(result.InstanceRankDetailId).IsEqualTo(41u);
        await Assert.That(result.InstanceId).IsEqualTo(69u);
        await Assert.That(result.Score).IsEqualTo(125u);
        await Assert.That(result.PlayResult).IsEqualTo(ExpeditionInstancePlayResult.Win);
        await Assert.That(result.RecordedAt).IsEqualTo(recordedAt);
        repository.GetInstanceHistories((uint)expedition.Id,
                SCExpeditionInstanceHistoryInfoListPacket.MaximumHistories)
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task OldRecipientSessionCannotConsumeOrCancelCurrentSessionsPendingSummon()
    {
        const uint recipientId = 120;
        var oldRecipient = CreateCharacter(recipientId);
        var currentRecipient = CreateCharacter(recipientId);
        var summoner = CreateCharacter(121);
        var expedition = new Expedition
        {
            Id = (FactionsEnum)704,
            Members =
            [
                new ExpeditionMember { CharacterId = recipientId },
                new ExpeditionMember { CharacterId = summoner.Id }
            ]
        };
        oldRecipient.Expedition = expedition;
        currentRecipient.Expedition = expedition;
        summoner.Expedition = expedition;
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(recipientId).Returns(currentRecipient);
        world.GetCharacterById(summoner.Id).Returns(summoner);
        var service = CreateService(Mock.Of<IExpeditionActivityRepository>().Object, world.Object);
        AddPendingSummon(service, currentRecipient, summoner, expedition, accepted: true);

        await Assert.That(service.CompleteSummon(oldRecipient)).IsFalse();
        await Assert.That(service.ReplyToSummon(oldRecipient, false, summoner.Name)).IsFalse();
        await Assert.That(GetPendingSummonCount(service)).IsEqualTo(1);
    }

    private static Character CreateCharacter(uint id)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id };
        character.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = character };
        return character;
    }

    private static ExpeditionActivityService CreateService(IExpeditionActivityRepository repository,
        IWorldManager world)
    {
        var expeditionManager = new ExpeditionManager(
            Mock.Of<IExpeditionIdManager>().Object,
            Mock.Of<ITeamManager>().Object,
            world,
            Mock.Of<IChatManager>().Object);
        return new ExpeditionActivityService(repository, world, expeditionManager,
            Mock.Of<IItemManager>().Object, Mock.Of<IExpeditionActivityConnectionFactory>().Object,
            Mock.Of<IFactionManager>().Object);
    }

    private static void ResetSingleton<T>() where T : class
    {
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
    }

    private static void AddPendingSummon(ExpeditionActivityService service, Character recipient,
        Character summoner, Expedition expedition, bool accepted)
    {
        var serviceType = typeof(ExpeditionActivityService);
        var destinationType = serviceType.GetNestedType("PendingDestination", BindingFlags.NonPublic)!;
        var pendingType = serviceType.GetNestedType("PendingExpeditionSummon", BindingFlags.NonPublic)!;
        var destination = Activator.CreateInstance(destinationType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [0u, null, 1f, 2f, 3f, 0f], null)!;
        var pending = Activator.CreateInstance(pendingType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [recipient, summoner, summoner.Name, (uint)expedition.Id, destination,
                DateTimeOffset.UtcNow.AddMinutes(1), accepted], null)!;
        var dictionary = serviceType.GetField("_pendingSummons", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;
        var added = (bool)dictionary.GetType().GetMethod("TryAdd")!.Invoke(dictionary, [recipient.Id, pending])!;
        if (!added)
            throw new InvalidOperationException("Failed to seed a pending summon.");
    }

    private static int GetPendingSummonCount(ExpeditionActivityService service)
    {
        var dictionary = typeof(ExpeditionActivityService)
            .GetField("_pendingSummons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        return (int)dictionary.GetType().GetProperty("Count")!.GetValue(dictionary)!;
    }
}
