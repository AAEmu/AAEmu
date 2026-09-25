using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The real <c>Portal.Delete()</c> path: deleting one member of a pair cascades to the other, so the
/// manager must not start a second delete for the member the cascade already removed.
/// </summary>
[NotInParallel]
public class OpenPortalCascadeTests
{
    private SingletonScope<WorldManager> _worlds;
    private WorldInstance _world;

    [Before(Test)]
    public void Setup()
    {
        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        _worlds = new SingletonScope<WorldManager>(worlds);

        var template = new WorldTemplate { Id = 1, Name = "a3-open-portal-cascade-test" };
        template.GeoData = new GeoDataManager(template);
        _world = new WorldInstance(template, 0, true, 91);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
    }

    [After(Test)]
    public void Teardown()
    {
        _world.Dispose();
        _worlds.Dispose();
    }

    [Test]
    public async Task LogoutStartsOneDelete_PortalDeleteCascadeRemovesTheLinkedPartner()
    {
        var manager = new CascadingPortalManager();
        var owner = new Character(new UnitCustomModelParams()) { Id = 7, ObjId = 7, Name = "Owner" };
        owner.Portals = new CharacterPortals(owner);
        var entry = new AAEmu.Game.Models.Game.Portal { Id = 501, Name = "camp", ZoneId = 1 };

        var entrance = AddPortal(entry.Id * 10 + 1, entry, isExit: false);
        var exit = AddPortal(entry.Id * 10 + 2, entry, isExit: true);
        manager.RegisterPair(owner, entrance, exit);

        manager.DeleteOwnerPortals(owner);

        // Portal.Delete marks itself before cascading, so the guard leaves the partner alone:
        // exactly one delete is started, while both units are gone from the world.
        await Assert.That(manager.DeleteCalls).IsEqualTo(1);
        await Assert.That(entrance.IsDeadOrDeleted).IsTrue();
        await Assert.That(exit.IsDeadOrDeleted).IsTrue();
        await Assert.That(entrance.IsDeleted).IsTrue();
        await Assert.That(exit.IsDeleted).IsTrue();
        await Assert.That(_world.GetNpc(entrance.ObjId)).IsNull();
        await Assert.That(_world.GetNpc(exit.ObjId)).IsNull();
    }

    [Test]
    public async Task DeletingOneBookEntryRemovesOnlyItsOwnPair()
    {
        var manager = new CascadingPortalManager();
        var owner = new Character(new UnitCustomModelParams()) { Id = 7, ObjId = 7, Name = "Owner" };
        owner.Portals = new CharacterPortals(owner);

        var first = new AAEmu.Game.Models.Game.Portal { Id = 11, Name = "first", ZoneId = 1 };
        var second = new AAEmu.Game.Models.Game.Portal { Id = 22, Name = "second", ZoneId = 1 };
        owner.Portals.PrivatePortals[first.Id] = first;
        owner.Portals.PrivatePortals[second.Id] = second;

        var firstEntrance = AddPortal(101, first, isExit: false);
        var firstExit = AddPortal(102, first, isExit: true);
        manager.RegisterPair(owner, firstEntrance, firstExit);
        var secondEntrance = AddPortal(201, second, isExit: false);
        var secondExit = AddPortal(202, second, isExit: true);
        manager.RegisterPair(owner, secondEntrance, secondExit);

        manager.DeletePortal(owner, type: 2, id: first.Id);

        await Assert.That(manager.DeleteCalls).IsEqualTo(1);
        await Assert.That(_world.GetNpc(firstEntrance.ObjId)).IsNull();
        await Assert.That(_world.GetNpc(firstExit.ObjId)).IsNull();
        await Assert.That(_world.GetNpc(secondEntrance.ObjId)).IsNotNull();
        await Assert.That(_world.GetNpc(secondExit.ObjId)).IsNotNull();
        await Assert.That(owner.Portals.PrivatePortals.ContainsKey(second.Id)).IsTrue();
    }

    private AAEmu.Game.Models.Game.Units.Portal AddPortal(uint objId, AAEmu.Game.Models.Game.Portal source, bool isExit)
    {
        var portal = new AAEmu.Game.Models.Game.Units.Portal
        {
            ObjId = objId,
            OwnerId = 7,
            Name = source.Name,
            IsExit = isExit,
            SourcePortal = source,
            TemplateId = 1,
            Template = new NpcTemplate { Id = 1, ModelId = 1, Level = 1 },
            ParentWorld = _world,
            Hp = 1,
            Mp = 1
        };
        _world.AddObject(portal);
        return portal;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field =
            typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    /// <summary>Counts manager-initiated deletes and then runs the real Portal.Delete().</summary>
    private sealed class CascadingPortalManager : PortalManager
    {
        public int DeleteCalls { get; private set; }

        public CascadingPortalManager()
            : base(Mock.Of<ILocalizationManager>().Object, Mock.Of<IWorldManager>().Object,
                Mock.Of<IZoneManager>().Object, Mock.Of<INpcManager>().Object, Mock.Of<IObjectIdManager>().Object)
        {
        }

        public void RegisterPair(Character owner, AAEmu.Game.Models.Game.Units.Portal entrance,
            AAEmu.Game.Models.Game.Units.Portal exit)
        {
            entrance.LinkedPortal = exit;
            exit.LinkedPortal = entrance;
            RegisterLivePortal(owner, entrance);
            RegisterLivePortal(owner, exit);
        }

        protected override void DeleteLivePortal(AAEmu.Game.Models.Game.Units.Portal portal)
        {
            DeleteCalls++;
            portal.Delete();
        }
    }
}
