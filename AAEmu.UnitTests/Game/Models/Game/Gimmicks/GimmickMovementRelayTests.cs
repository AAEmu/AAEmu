using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

using TUnit.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Gimmicks;

/// <summary>
/// The publisher half of the gimmick movement relay: a world that drives a gimmick has to hand the
/// transform to the zone, because the zone's own movement report for a world-owned gimmick is
/// dropped rather than forwarded — without this the zone keeps simulating the object where it was
/// created, for ever.
/// </summary>
[NotInParallel]
public class GimmickMovementRelayTests
{
    private const uint OwningZoneId = 186;
    private const uint GimmickTemplateId = 4242;
    private static readonly TimeSpan TickDelta = TimeSpan.FromMilliseconds(50);

    private SingletonScope<WorldManager> _worlds;
    private WorldInstance _world;
    private List<(GimmickMovementData Data, uint ZoneId)> _published;
    private Action<GimmickMovementData, uint> _previousRelay;

    [Before(Test)]
    public void Setup()
    {
        _published = [];
        _previousRelay = WorldIntegration.RelayGimmickMovementToZone;
        WorldIntegration.RelayGimmickMovementToZone = (data, zoneId) => _published.Add((data, zoneId));

        var objectIds = new NonUnitObjectIdManager();
        var gimmickIds = new GimmickIdManager();
        if (!objectIds.Initialize() || !gimmickIds.Initialize())
            throw new InvalidOperationException("the id allocators could not be primed for the test");
        SetInstance(objectIds);
        SetInstance(gimmickIds);

        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        _worlds = new SingletonScope<WorldManager>(worlds);

        var template = new WorldTemplate { Id = 1, Name = "a4-gimmick-movement-relay" };
        template.GeoData = new GeoDataManager(template);
        _world = new WorldInstance(template, 0, true, 1);
        _world.GimmickManager = new GimmickManager(_world);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
    }

    [After(Test)]
    public void Teardown()
    {
        WorldIntegration.RelayGimmickMovementToZone = _previousRelay;
        ClearInstance<NonUnitObjectIdManager>();
        ClearInstance<GimmickIdManager>();
        _worlds.Dispose();
    }

    [Test]
    public async Task ATickThatMovesTheGimmick_PublishesItToTheOwningZone()
    {
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        MoveTo(gimmick, 1000f, 2000f, 34f);

        gimmick.GimmickTick(TickDelta);

        await Assert.That(_published.Count).IsEqualTo(1);
        await Assert.That(_published[0].ZoneId).IsEqualTo(OwningZoneId);
        await Assert.That(_published[0].Data.Id).IsEqualTo(gimmick.ObjId);
    }

    [Test]
    public async Task ThePublishedRecord_CarriesTheZoneReadableTransform()
    {
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        MoveTo(gimmick, 1000f, 2000f, 34f);

        gimmick.GimmickTick(TickDelta);

        var data = _published[0].Data;
        await Assert.That(Helpers.ConvertLongX(data.X)).IsEqualTo(1000f);
        await Assert.That(Helpers.ConvertLongY(data.Y)).IsEqualTo(2000f);
        await Assert.That(data.Z).IsEqualTo(34f);
        await Assert.That(data.Time).IsEqualTo(gimmick.Time);
    }

    [Test]
    public async Task ThePublishedRecord_CarriesTheVelocityTheZoneIntegratesWith()
    {
        // The zone's rigid body is driven from the reported velocity, so a record that carried a
        // zero or a bare per-tick distance would leave the object standing still.
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        Park(gimmick);
        MoveTo(gimmick, 1000f, 2000f, 34f);

        gimmick.GimmickTick(TickDelta);

        var expectedZ = 4f / (float)TickDelta.TotalSeconds;
        await Assert.That(_published[0].Data.Velocity.Z).IsEqualTo(expectedZ);
    }

    [Test]
    public async Task ATickThatChangesNothing_PublishesNothing()
    {
        // A lift that is parked still runs its tick; relaying every one of those would put a packet
        // on the wire for an object that did not move.
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        Park(gimmick);

        gimmick.GimmickTick(TickDelta);

        await Assert.That(_published).IsEmpty();
    }

    [Test]
    public async Task RepeatedParkedTicks_NeverPublish()
    {
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        Park(gimmick);

        for (var i = 0; i < 20; i++)
            gimmick.GimmickTick(TickDelta);

        await Assert.That(_published).IsEmpty();
    }

    [Test]
    public async Task AMovingGimmick_KeepsPublishingAsItTravels()
    {
        var gimmick = CreateGimmick(1000f, 2000f, 30f);

        for (var step = 1; step <= 3; step++)
        {
            MoveTo(gimmick, 1000f, 2000f, 30f + step * 4f);
            gimmick.GimmickTick(TickDelta);
        }

        await Assert.That(_published.Count).IsEqualTo(3);
        await Assert.That(_published[^1].Data.Z).IsEqualTo(42f);
    }

    [Test]
    public async Task WithNoZoneRelay_Wired_TheTickStillCompletes()
    {
        // The relay is optional outside zone authority; a null publisher must not take the tick down.
        WorldIntegration.RelayGimmickMovementToZone = null;
        var gimmick = CreateGimmick(1000f, 2000f, 30f);
        MoveTo(gimmick, 1000f, 2000f, 34f);

        gimmick.GimmickTick(TickDelta);

        await Assert.That(gimmick.Transform.World.Position.Z).IsEqualTo(34f);
    }

    private Gimmick CreateGimmick(float x, float y, float z)
    {
        var template = new WorldTemplate { Id = 1, Name = "a4-gimmick-movement-relay" };
        template.GeoData = new GeoDataManager(template);
        _world.GimmickManager = new GimmickManager(_world);

        var gimmick = new Gimmick
        {
            ParentWorld = _world,
            Template = new GimmickTemplate { Id = GimmickTemplateId, ModelPath = "gameobjects/test/lift.ddf" },
            TemplateId = GimmickTemplateId,
            ModelPath = "gameobjects/test/lift.ddf",
            ObjId = NonUnitObjectIdManager.Instance.GetNextId(),
            Faction = new AAEmu.Game.Models.Game.Faction.SystemFaction(),
        };
        gimmick.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);
        // Assign the backing field rather than the property: the property's setter runs the
        // zone-change path, which needs the ZoneManager DI singleton this test has no reason to build.
        SetField(gimmick.Transform, "_zoneId", OwningZoneId);
        return gimmick;
    }

    private static void MoveTo(Gimmick gimmick, float x, float y, float z) =>
        gimmick.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);

    /// <summary>
    /// Settles the tick's own "what did I publish last time" memory onto where the object already
    /// is, so the next tick measures a delta against a known parked baseline instead of against the
    /// origin the record starts life with.
    /// </summary>
    private static void Park(Gimmick gimmick)
    {
        SetProperty(gimmick, "LastPos", gimmick.Transform.World.Position);
        SetProperty(gimmick, "LastRot", gimmick.Transform.World.Rotation);
    }

    private static void SetProperty(object target, string name, object value) =>
        target.GetType()
            .GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static void SetInstance<T>(T value) where T : class =>
        typeof(T).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, value);

    private static void ClearInstance<T>() where T : class =>
        typeof(T).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
}
