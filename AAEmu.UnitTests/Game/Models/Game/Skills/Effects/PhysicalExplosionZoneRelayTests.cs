using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;
using AAEmu.World.Core.Packets.Wz;

using TUnit.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// A physical explosion has no publisher of its own: the blast is handed to the zone as the
/// per-unit impulse it produces, which is the path the zone already simulates every hull through.
/// This pins that path end to end — the decision (who is caught, how hard, which way) and the bytes
/// the zone reads — so it cannot regress silently into a visual-only effect.
/// </summary>
[NotInParallel]
public class PhysicalExplosionZoneRelayTests
{
    // Authored ranges are millimetres, like every other range in the data; radii below are written
    // in metres and converted at the effect boundary, which is what the test asserts against.
    private const float RadiusMetres = 10f;
    private const float Pressure = 8f;
    private const float NearDistance = 2f;
    private const float FarDistance = 5f;
    private const float OutsideDistance = 20f;

    private const uint TargetObjId = 100u;
    private const uint NearObjId = 101u;
    private const uint FarObjId = 102u;
    private const uint OutsideObjId = 103u;
    private const uint CasterObjId = 104u;

    private const float Tolerance = 1e-4f;

    private SingletonScope<WorldManager> _worlds;
    private WorldManager _worldsManager;
    private WorldInstance _world;
    private List<(uint ObjId, float[] Vel)> _impulses;
    private Action<uint, SkillCaster, float[], float[], float[], float[]> _previousRelay;

    [Before(Test)]
    public void Setup()
    {
        _impulses = [];
        _previousRelay = WorldIntegration.RelayImpulseToZone;
        WorldIntegration.RelayImpulseToZone = (objId, _, vel, _, _, _) => _impulses.Add((objId, vel));

        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        _worlds = new SingletonScope<WorldManager>(worlds);
        _worldsManager = worlds;

        var template = new WorldTemplate { Id = 1, Name = "a4-physical-explosion-relay", CellX = 1, CellY = 1 };
        template.GeoData = new GeoDataManager(template);
        _world = new WorldInstance(template, 0, true, 1);
        BuildRegions(template);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
    }

    /// <summary>
    /// The blast asks the world who is nearby, which reads the region grid. A world instance only
    /// gets one when the manager creates the instance from a real template, so build the grid here
    /// over the same cell dimensions the template declares.
    /// </summary>
    private void BuildRegions(WorldTemplate template)
    {
        var side = template.CellX * WorldManager.SECTORS_PER_CELL;
        _world.Regions = new Region[side, side];
        // Region placement samples the owning zone for the position; an all-zero table keeps the
        // object on the zone key it already has instead of reassigning one.
        template.ZoneKeyByRegions = new uint[side, side];
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
            _world.Regions[x, y] = new Region(_world, x, y, 0);
    }

    [After(Test)]
    public void Teardown()
    {
        WorldIntegration.RelayImpulseToZone = _previousRelay;
        _worlds.Dispose();
    }

    [Test]
    public async Task TheBlastReachesTheZone_ForEveryBodyItCatches()
    {
        Place();

        Apply();

        await Assert.That(_impulses.Count).IsEqualTo(2);
        await Assert.That(_impulses.Select(i => i.ObjId)).Contains(NearObjId);
        await Assert.That(_impulses.Select(i => i.ObjId)).Contains(FarObjId);
    }

    [Test]
    public async Task ABodyBeyondTheRadius_IsNotImpulsed()
    {
        Place();

        Apply();

        await Assert.That(_impulses.Select(i => i.ObjId)).DoesNotContain(OutsideObjId);
    }

    [Test]
    public async Task TheBlastPushesEachBodyAwayFromItsCentre()
    {
        Place();

        Apply();

        var near = VelocityFor(NearObjId);
        await Assert.That(near[0]).IsEqualTo(Pressure * (1f - NearDistance / RadiusMetres)).Within(Tolerance);
        await Assert.That(near[1]).IsEqualTo(0f).Within(Tolerance);
        await Assert.That(near[2]).IsEqualTo(0f).Within(Tolerance);
    }

    [Test]
    public async Task TheBlastFallsOffLinearlyTowardsTheRim()
    {
        // A blast that reached the rim at full strength would throw the far body as hard as the near
        // one; the authored falloff is what separates them.
        Place();

        Apply();

        var expectedNear = Pressure * (1f - NearDistance / RadiusMetres);
        var expectedFar = Pressure * (1f - FarDistance / RadiusMetres);
        await Assert.That(VelocityFor(NearObjId)[0]).IsEqualTo(expectedNear).Within(Tolerance);
        await Assert.That(VelocityFor(FarObjId)[0]).IsEqualTo(expectedFar).Within(Tolerance);
        await Assert.That(VelocityFor(NearObjId)[0]).IsGreaterThan(VelocityFor(FarObjId)[0]);
    }

    [Test]
    public async Task TheImpulseIsScaledByTheAuthoredPressure()
    {
        // Same geometry, double the authored pressure: the impulse the zone sees doubles.
        Place();
        Apply(pressure: Pressure * 2f);

        var expected = Pressure * 2f * (1f - NearDistance / RadiusMetres);
        await Assert.That(VelocityFor(NearObjId)[0]).IsEqualTo(expected).Within(Tolerance);
    }

    [Test]
    public async Task ABlastWithNoUsableRadius_ImpulsesNobody()
    {
        Place();

        Apply(radiusMetres: 0f);

        await Assert.That(_impulses).IsEmpty();
    }

    [Test]
    public async Task TheDeliveredValuesSerializeOntoTheZoneWire()
    {
        // The relay hands the zone a unit impulse packet; the values the blast computed have to be
        // the ones the zone reads back, in the zone's field order.
        var vel = new[] { 6.5f, -1.25f, 0.75f };
        var frame = new PacketStream(new WZImpulseUnitPacket(
            TargetObjId, new SkillCasterUnit(CasterObjId),
            vel[0], vel[1], vel[2],
            0f, 0f, 0f,
            vel[0], vel[1], vel[2],
            0f, 0f, 0f).Encode());

        frame.ReadUInt16();
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.ImpulseUnit);
        await Assert.That(frame.ReadBc()).IsEqualTo(TargetObjId);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)SkillCasterType.Unit);
        await Assert.That(frame.ReadBc()).IsEqualTo(CasterObjId);
        await Assert.That(frame.ReadSingle()).IsEqualTo(6.5f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(-1.25f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0.75f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(6.5f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(-1.25f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0.75f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(0f);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    private float[] VelocityFor(uint objId) =>
        _impulses.Single(i => i.ObjId == objId).Vel;

    private void Place()
    {
        PlaceUnit(TargetObjId, 0f, 0f, 0f);
        PlaceUnit(NearObjId, NearDistance, 0f, 0f);
        PlaceUnit(FarObjId, FarDistance, 0f, 0f);
        PlaceUnit(OutsideObjId, OutsideDistance, 0f, 0f);
    }

    private void PlaceUnit(uint objId, float x, float y, float z)
    {
        var unit = new Unit { ObjId = objId, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100 };
        unit.ParentWorld = _world;
        unit.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);
        _world.AddObject(unit);
        _worldsManager.AddVisibleObject(unit);
    }

    private void Apply(float? radiusMetres = null, float? pressure = null)
    {
        var target = _world.GetUnit(TargetObjId);
        var effect = new PhysicalExplosionEffect
        {
            Radius = (radiusMetres ?? RadiusMetres) * 1000f,
            Pressure = pressure ?? Pressure
        };
        effect.Apply(
            _world.GetUnit(CasterObjId) ?? target,
            new SkillCasterUnit(CasterObjId),
            target,
            new SkillCastUnitTarget(TargetObjId),
            new CastSkill(0, 1),
            new EffectSource(),
            new SkillObject(),
            DateTime.UtcNow);
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
