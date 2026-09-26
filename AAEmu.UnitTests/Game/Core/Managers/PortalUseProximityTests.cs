using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

// This file's namespace sits under AAEmu.UnitTests.Game, where a bare `Models` would bind to the
// test tree rather than to AAEmu.Game, so the two portal types are named explicitly.
using BookPortal = AAEmu.Game.Models.Game.Portal;
using PortalUnit = AAEmu.Game.Models.Game.Units.Portal;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The object id in <c>CSUsePortal</c> is client supplied and resolves any open portal in the world,
/// and a pair now stays up until the owner deletes it, logs out or disconnects. Without a reachability
/// guard a hand made use teleports the sender to any open pair's destination from anywhere.
/// </summary>
/// <remarks>
/// The guard is the region neighbourhood the client could have seen the portal in, because no shipped
/// content provides a radius to walk a portal from. These tests drive the real
/// <see cref="PortalManager.UsePortal"/> and observe whether the landing was reached, so removing the
/// guard from the manager fails them.
/// </remarks>
[NotInParallel]
public sealed class PortalUseProximityTests
{
    private const uint PlayerObjId = 8101;
    private const uint PortalObjId = 8102;
    private const uint SyntheticWorldId = 8103;
    private const uint SyntheticZoneKey = 8104;

    /// <summary>WorldManager.REGION_SIZE.</summary>
    private const float RegionSize = 64f;

    private IDisposable _worldScope;
    private IDisposable _skillScope;
    private WorldInstance _world;
    private RecordingPortalManager _manager;
    private Character _player;

    [Before(Test)]
    public void Setup()
    {
        _worldScope = TestDungeonWorld.InstallWorldManager();
        // The overburdened check on the accepting path reads Buffs, which reaches SkillManager, and
        // SkillManager has no parameterless constructor. Another test's scope can leave that field
        // null, so the scope is held here rather than relying on the assembly hook alone.
        _skillScope = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        _world = TestDungeonWorld.CreateWorld(SyntheticWorldId, 0, SyntheticZoneKey);
        _manager = new RecordingPortalManager(
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<INpcManager>().Object,
            Mock.Of<IObjectIdManager>().Object);

        _player = new Character(new UnitCustomModelParams())
        {
            Id = PlayerObjId, ObjId = PlayerObjId, Name = "Walker"
        };
        Place(_player, 100f, 100f, 0f);
        TestDungeonWorld.Enter(_world, _player);
    }

    [After(Test)]
    public void Teardown()
    {
        _world?.Dispose();
        _skillScope?.Dispose();
        _worldScope?.Dispose();
    }

    [Test]
    public async Task UsePortal_UsesThePortalTheCharacterIsStandingOn()
    {
        var portal = SpawnPortal(100f, 100f, 0f);

        _manager.UsePortal(_player, portal.ObjId);

        await Assert.That(_manager.Landings).HasCount().EqualTo(1);
        await Assert.That(_manager.Landings[0].Portal).IsSameReferenceAs(portal);
        await Assert.That(_manager.Landings[0].Character).IsSameReferenceAs(_player);
    }

    [Test]
    public async Task UsePortal_RefusesAPortalInAnotherRegionNeighbourhood()
    {
        // Far enough that the two are not in the same neighbourhood: the client cannot have collided
        // with this portal, so the use is a forged id.
        var portal = SpawnPortal(100f + RegionSize * 8f, 100f, 0f);

        _manager.UsePortal(_player, portal.ObjId);

        await Assert.That(_manager.Landings).IsEmpty();
    }

    [Test]
    public async Task UsePortal_RefusesBeforeTheOwnershipFlagIsEvenRead()
    {
        // The reachability guard runs first, so a remote probe does not get an answer that depends on
        // the portal's owner: the pair stays where it is and nothing is landed.
        var portal = SpawnPortal(100f + RegionSize * 8f, 100f, 0f, new BookPortal
        {
            Id = 42, Name = "remote", ZoneId = 1, X = 1f, Y = 2f, Z = 3f, Owner = _player.Id + 1
        });

        _manager.UsePortal(_player, portal.ObjId, onlyMyPortal: true);

        await Assert.That(_manager.Landings).IsEmpty();
    }

    [Test]
    public async Task UsePortal_RefusesACharacterTheWorldHasNotPlacedInARegion()
    {
        // No region means the world never positioned the character, so the use cannot be attributed to
        // a collision. Failing towards refusing keeps the guard from trusting an unplaced unit.
        var portal = SpawnPortal(100f, 100f, 0f);
        _player.Region = null;

        _manager.UsePortal(_player, portal.ObjId);

        await Assert.That(_manager.Landings).IsEmpty();
    }

    [Test]
    public async Task UsePortal_StillRefusesAnUnknownObjectId()
    {
        _manager.UsePortal(_player, PortalObjId + 1);

        await Assert.That(_manager.Landings).IsEmpty();
    }

    /// <summary>
    /// Puts a unit in the world at a position and gives it a region, the way an entry does. The region
    /// a unit is placed in is what the neighbourhood guard reads.
    /// </summary>
    private void Place(GameObject unit, float x, float y, float z)
    {
        unit.ParentWorld = _world;
        unit.Transform.Local.SetPosition(x, y, z);
        unit.Transform.KeepZoneQuietly(SyntheticZoneKey);
        var region = new Region(_world, (int)(x / RegionSize), (int)(y / RegionSize), SyntheticZoneKey);
        region.AddObject(unit);
        unit.Region = region;
        unit.IsVisible = true;
    }

    private PortalUnit SpawnPortal(float x, float y, float z, BookPortal sourcePortal = null)
    {
        var portal = new PortalUnit
        {
            ObjId = PortalObjId,
            OwnerId = _player.Id,
            Name = "camp",
            Hp = 1,
            SourcePortal = sourcePortal,
            TeleportPosition = new AAEmu.Game.Models.Game.World.Transform.Transform(
                null, null, 1, 0, 400f, 400f, 0f, 0f)
        };
        Place(portal, x, y, z);
        _world.SetNpc(portal.ObjId, portal);
        return portal;
    }

    private sealed record Landing(Character Character, PortalUnit Portal);

    private sealed class RecordingPortalManager(
        ILocalizationManager localizationManager,
        IWorldManager worldManager,
        IZoneManager zoneManager,
        INpcManager npcManager,
        IObjectIdManager objectIdManager)
        : PortalManager(localizationManager, worldManager, zoneManager, npcManager, objectIdManager)
    {
        public List<Landing> Landings { get; } = [];

        protected override void ApplyPortalUse(Character character, PortalUnit portal) =>
            Landings.Add(new Landing(character, portal));
    }
}
