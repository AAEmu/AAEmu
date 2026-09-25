using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class OpenPortalLifecycleTests
{
    private const uint BookPortalId = 501;
    private const uint EnterNpcId = 701;
    private const uint ExitNpcId = 702;

    [Test]
    public async Task OpenPortal_RegistersContentResolvedEntranceAndExitAndLogoutRemovesBoth()
    {
        var (manager, owner) = CreateManager();
        var effect = CreateEffect();

        manager.OpenPortal(owner, new SkillObjectUnk1 { Id = (int)BookPortalId, X = 10f, Y = 20f, Z = 30f }, effect);

        await Assert.That(manager.Created).HasCount().EqualTo(2);
        var entrance = manager.Created[0];
        var exit = manager.Created[1];
        await Assert.That(entrance.IsExit).IsFalse();
        await Assert.That(exit.IsExit).IsTrue();
        await Assert.That(entrance.SourcePortal.Id).IsEqualTo(BookPortalId);
        await Assert.That(exit.SourcePortal.Id).IsEqualTo(BookPortalId);
        await Assert.That(entrance.LinkedPortal).IsSameReferenceAs(exit);
        await Assert.That(exit.LinkedPortal).IsSameReferenceAs(entrance);

        manager.DeleteOwnerPortals(owner);

        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, entrance))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, exit))).IsTrue();
        manager.DeleteOwnerPortals(owner);
        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
    }

    [Test]
    public async Task DeletePortal_RemovesTheBookEntryAndItsLivePortals()
    {
        var (manager, owner) = CreateManager();
        manager.OpenPortal(owner, new SkillObjectUnk1 { Id = (int)BookPortalId, X = 10f, Y = 20f, Z = 30f }, CreateEffect());
        await Assert.That(owner.Portals.PrivatePortals.ContainsKey(BookPortalId)).IsTrue();

        manager.DeletePortal(owner, type: 2, id: BookPortalId);

        await Assert.That(owner.Portals.PrivatePortals.ContainsKey(BookPortalId)).IsFalse();
        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
    }

    [Test]
    public async Task DeletePortal_OnlyRemovesThePairOfTheEntryItResolved()
    {
        // A private book id and a district_return_points id share one id space, so the same number
        // names both entries at once. Deleting one must leave the other entry's live pair alone.
        const uint sharedId = 4097;
        var (manager, owner) = CreateManager();

        var districtEntry = new AAEmu.Game.Models.Game.Portal
        {
            Id = sharedId, Type = 77, Name = "district", ZoneId = 1, X = 1f, Y = 2f, Z = 3f
        };
        var privateEntry = new AAEmu.Game.Models.Game.Portal
        {
            Id = sharedId, Name = "private", ZoneId = 1, X = 4f, Y = 5f, Z = 6f
        };
        owner.Portals.DistrictPortals[sharedId] = districtEntry;
        owner.Portals.PrivatePortals[sharedId] = privateEntry;

        var (districtEntrance, districtExit) = manager.RegisterPair(owner, districtEntry);
        var (privateEntrance, privateExit) = manager.RegisterPair(owner, privateEntry);

        // The district type names the district entry, so this delete acts on the district pair.
        manager.DeletePortal(owner, type: 1, id: sharedId);

        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, districtEntrance))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, districtExit))).IsTrue();

        // The private pair shares the numeric id but not the entry, so it is untouched and still
        // reachable: a logout must still remove it.
        manager.DeleteOwnerPortals(owner);
        await Assert.That(manager.Deleted).HasCount().EqualTo(4);
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, privateEntrance))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, privateExit))).IsTrue();
    }

    [Test]
    public async Task DeletePortal_PrivateRequestOnASharedIdRemovesThePrivatePairOnly()
    {
        // Mirror of the district case: the same two entries, but the client names the private one.
        // A type-agnostic lookup answers from the district book first and kills the wrong pair.
        const uint sharedId = 4097;
        var (manager, owner) = CreateManager();

        var districtEntry = new AAEmu.Game.Models.Game.Portal
        {
            Id = sharedId, Type = 77, Name = "district", ZoneId = 1, X = 1f, Y = 2f, Z = 3f
        };
        var privateEntry = new AAEmu.Game.Models.Game.Portal
        {
            Id = sharedId, Name = "private", ZoneId = 1, X = 4f, Y = 5f, Z = 6f
        };
        owner.Portals.DistrictPortals[sharedId] = districtEntry;
        owner.Portals.PrivatePortals[sharedId] = privateEntry;

        var (districtEntrance, districtExit) = manager.RegisterPair(owner, districtEntry);
        var (privateEntrance, privateExit) = manager.RegisterPair(owner, privateEntry);

        manager.DeletePortal(owner, type: 2, id: sharedId);

        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, privateEntrance))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, privateExit))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, districtEntrance))).IsFalse();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, districtExit))).IsFalse();

        // The private book row is gone; the district entry and its live pair are untouched.
        await Assert.That(owner.Portals.PrivatePortals.ContainsKey(sharedId)).IsFalse();
        await Assert.That(owner.Portals.DistrictPortals.ContainsKey(sharedId)).IsTrue();
    }

    [Test]
    public async Task DeletePortal_ReturnPointTypeFallbackStillResolvesDistrictEntries()
    {
        // A district entry is addressed either by its own id or by the return-point id in Type.
        // That fallback is what makes a return-point delete work and must survive the typed lookup.
        const uint districtId = 33;
        const uint returnPointId = 900;
        var (manager, owner) = CreateManager();

        var districtEntry = new AAEmu.Game.Models.Game.Portal
        {
            Id = districtId, Type = returnPointId, Name = "district", ZoneId = 1, X = 1f, Y = 2f, Z = 3f
        };
        owner.Portals.DistrictPortals[districtId] = districtEntry;

        var (entrance, exit) = manager.RegisterPair(owner, districtEntry);
        manager.DeletePortal(owner, type: 1, id: returnPointId);

        await Assert.That(manager.Deleted).HasCount().EqualTo(2);
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, entrance))).IsTrue();
        await Assert.That(manager.Deleted.Any(portal => ReferenceEquals(portal, exit))).IsTrue();
    }

    private static OpenPortalEffect CreateEffect() => new()
    {
        Id = 1,
        Distance = 3f,
        EnterPortalNpcId = EnterNpcId,
        ExitPortalNpcId = ExitNpcId
    };

    private static (RecordingPortalManager Manager, Character Owner) CreateManager()
    {
        var npcManager = Mock.Of<INpcManager>();
        npcManager.GetTemplate(EnterNpcId).Returns(new NpcTemplate { Id = EnterNpcId, ModelId = 1, Level = 1 });
        npcManager.GetTemplate(ExitNpcId).Returns(new NpcTemplate { Id = ExitNpcId, ModelId = 2, Level = 1 });

        var manager = new RecordingPortalManager(npcManager.Object);
        var owner = new Character(new UnitCustomModelParams()) { Id = 7, ObjId = 7, Name = "Owner" };
        owner.Portals = new CharacterPortals(owner);
        owner.Portals.PrivatePortals[BookPortalId] = new AAEmu.Game.Models.Game.Portal
        {
            Id = BookPortalId,
            Name = "camp",
            ZoneId = 1,
            X = 10f,
            Y = 20f,
            Z = 30f
        };
        return (manager, owner);
    }

    private sealed class RecordingPortalManager(INpcManager npcManager)
        : PortalManager(Mock.Of<ILocalizationManager>().Object, Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object, npcManager, Mock.Of<IObjectIdManager>().Object)
    {
        public List<AAEmu.Game.Models.Game.Units.Portal> Created { get; } = [];
        public List<AAEmu.Game.Models.Game.Units.Portal> Deleted { get; } = [];
        private uint _nextObjId = 9000;

        protected override bool CheckCanOpenPortal(Character owner, uint targetZoneId, uint openPortalEffectId) => true;

        protected override AAEmu.Game.Models.Game.Units.Portal MakePortal(Character owner, bool isExit,
            AAEmu.Game.Models.Game.Portal portalInfo, SkillObjectUnk1 portalEffectObj, uint templateId)
        {
            var portal = new AAEmu.Game.Models.Game.Units.Portal
            {
                ObjId = _nextObjId++,
                OwnerId = owner.Id,
                Name = portalInfo.Name,
                IsExit = isExit,
                SourcePortal = portalInfo,
                Hp = 1,
                ParentWorld = owner.ParentWorld
            };
            Created.Add(portal);
            return portal;
        }

        protected override void DeleteLivePortal(AAEmu.Game.Models.Game.Units.Portal portal) => Deleted.Add(portal);

        /// <summary>Registers a live pair the book already owns, bypassing the cast path.</summary>
        public (AAEmu.Game.Models.Game.Units.Portal Entrance, AAEmu.Game.Models.Game.Units.Portal Exit) RegisterPair(
            Character owner, AAEmu.Game.Models.Game.Portal sourcePortal)
        {
            var entrance = new AAEmu.Game.Models.Game.Units.Portal
            {
                ObjId = _nextObjId++,
                OwnerId = owner.Id,
                Name = sourcePortal.Name,
                SourcePortal = sourcePortal,
                Hp = 1,
                ParentWorld = owner.ParentWorld
            };
            var exit = new AAEmu.Game.Models.Game.Units.Portal
            {
                ObjId = _nextObjId++,
                OwnerId = owner.Id,
                Name = sourcePortal.Name,
                IsExit = true,
                SourcePortal = sourcePortal,
                Hp = 1,
                ParentWorld = owner.ParentWorld
            };
            entrance.LinkedPortal = exit;
            exit.LinkedPortal = entrance;
            RegisterLivePortal(owner, entrance);
            RegisterLivePortal(owner, exit);
            return (entrance, exit);
        }
    }
}
