using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

using System.Numerics;
using System.Reflection;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// The real <see cref="DoodadAreaTriggerRuntime.OnZoneAreaEvent"/> entry point, driven with a
/// world and a character so the Zone-ownership guards and the leave-edge release run through
/// the same code the World server calls.
/// </summary>
[NotInParallel]
public class DoodadAreaTriggerRelayTests
{
    private const uint ZoneId = 133;
    private const uint OtherZoneId = 200;
    private const uint GroupId = 0x16;
    private const uint UnitId = 0x010203;
    private const uint UnknownUnitId = 0x0BADF00D;

    /// <summary>A real area id as the dedicated writes it: an id, never a distance.</summary>
    private const int AreaId = 342;

    private const uint OtherAreaId = 343;
    private const uint DoodadObjId = 900;

    private IDisposable _worldScope;
    private IDisposable _modelScope;
    private WorldInstance _world;
    private Character _character;
    private Doodad _doodad;
    private bool _previousZoneAuthority;

    [Before(Test)]
    public void BuildWorld()
    {
        _worldScope = TestDungeonWorld.InstallWorldManager();
        _modelScope = new SingletonScope<ModelManager>(new ModelManager());
        SeedEmptyModelCatalog();
        _world = TestDungeonWorld.CreateSizedWorld(5001, 0, 1, 1, ZoneId);

        _character = new Character(new UnitCustomModelParams()) { ObjId = UnitId, Name = "area-edge-tester" };
        SetZone(_character, ZoneId);
        _world.AddObject(_character);

        _doodad = new Doodad
        {
            ObjId = DoodadObjId,
            TemplateId = 1202,
        };

        Place(_character, 0f, 0f);
        Place(_doodad, 2f, 0f);

        _previousZoneAuthority = WorldIntegration.ZoneAuthority;
        WorldIntegration.ZoneAuthority = true;
        AreaTriggerManager.Instance.AreaEdges.Reset();
    }

    [After(Test)]
    public void TearDownWorld()
    {
        WorldIntegration.ZoneAuthority = _previousZoneAuthority;
        AreaTriggerManager.Instance.AreaEdges.Reset();
        _modelScope?.Dispose();
        _worldScope?.Dispose();
    }

    private static void SeedEmptyModelCatalog()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(ModelManager).GetField("_modelTypes", flags)
            ?.SetValue(ModelManager.Instance, new Dictionary<uint, ModelType>());
        typeof(ModelManager).GetField("_models", flags)
            ?.SetValue(ModelManager.Instance, new Dictionary<string, Dictionary<uint, Model>>());
    }

    [Test]
    public async Task LeaveEdge_ReleasesTheMembershipThroughTheRealEntryPoint()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, AreaId, DoodadObjId);
        edges.TryTransition(key, entering: true);
        await Assert.That(edges.IsInside(key)).IsTrue();

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, AreaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsFalse();
    }

    /// <summary>
    /// The regression this key shape exists for: the group is the area KIND and is the
    /// same on every area of the family, so leaving one area must not release the unit's
    /// membership of a DIFFERENT area reported under the same group. A key built from the
    /// group alone drops both.
    /// </summary>
    [Test]
    public async Task LeaveEdge_ForOneAreaKeepsTheUnitsMembershipOfAnotherAreaOfTheSameGroup()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var left = new AreaEdgeKey(ZoneId, UnitId, GroupId, AreaId, DoodadObjId);
        var stayed = new AreaEdgeKey(ZoneId, UnitId, GroupId, OtherAreaId, DoodadObjId);
        edges.TryTransition(left, entering: true);
        edges.TryTransition(stayed, entering: true);
        await Assert.That(edges.IsInside(left)).IsTrue();
        await Assert.That(edges.IsInside(stayed)).IsTrue();

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, AreaId, 0, entering: false);

        await Assert.That(edges.IsInside(left)).IsFalse();
        await Assert.That(edges.IsInside(stayed)).IsTrue();
    }

    [Test]
    public async Task LeaveEdge_ForAnotherZoneDoesNotReleaseThisZonesMembership()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, AreaId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(OtherZoneId, UnitId, GroupId, AreaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task Edges_AreIgnoredWithoutZoneAuthority()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, AreaId, DoodadObjId);
        edges.TryTransition(key, entering: true);
        WorldIntegration.ZoneAuthority = false;

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, AreaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task LeaveEdge_ForAnUnknownUnitDoesNotReleaseAnything()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnknownUnitId, GroupId, AreaId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnknownUnitId, GroupId, AreaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    [Arguments(0, GroupId)]
    [Arguments(-1, GroupId)]
    [Arguments(AreaId, 0u)]
    public async Task Edges_WithoutAUsableGroupAndAreaIdAreIgnored(int areaId, uint groupId)
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, groupId, (uint)areaId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, groupId, areaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task EnterEdge_ClaimsNothingAndIsNotEvenAScan()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        // This used to pass because no DoodadManager content existed in this scope, so the
        // row resolved to no template. It now passes for a stronger reason: the entry point
        // no longer scans or dispatches at all, because no area kind the Zone sends
        // identifies a doodad. Asserted directly by the test below.
        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, AreaId, 0, entering: true);

        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, (uint)AreaId, DoodadObjId)))
            .IsFalse();
    }

    /// <summary>
    /// Pins the removal of dispatch from the area-edge entry point.
    /// <para>
    /// This is the regression for a live defect, not a style choice. The Zone sends area
    /// kinds 16, 19, 20, 21 and 22 whose <c>value1</c> is a <c>spheres.id</c> or a
    /// <c>districts.id</c>; kind 21 resolves through <c>sphere_doodad_interacts</c>
    /// (<c>id, skill_id, doodad_family_id</c>), which has no area column and does not
    /// reference <c>doodad_func_area_triggers</c>. No kind therefore names a doodad, so
    /// there is no predicate to dispatch on.
    /// </para>
    /// <para>
    /// The test walks EVERY doodad id across the plausible range rather than one, because
    /// a single-id assertion can be satisfied by a dispatch that happened to miss. If
    /// dispatch is ever restored before the binding is known, one of these will be claimed.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnEnterEdgeClaimsNoMembershipForAnyDoodad()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        for (uint doodadId = 1; doodadId <= 64; doodadId++)
            await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, (uint)AreaId, doodadId)))
                .IsFalse();

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, AreaId, 0, entering: true);

        for (uint doodadId = 1; doodadId <= 64; doodadId++)
            await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, (uint)AreaId, doodadId)))
                .IsFalse();
    }

    /// <summary>
    /// Pins that the area id is an id and not a length: an area whose id is 1464 metres
    /// wide is not a thing, and the edge must still be accepted and keyed on that id.
    /// Under a radius reading this id would be a 1.4 km scan.
    /// </summary>
    [Test]
    public async Task AreaIdIsAnIdentifierAndIsNotTreatedAsADistance()
    {
        const int largeAreaId = 1464;
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, (uint)largeAreaId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, largeAreaId, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsFalse();
        await Assert.That(DoodadAreaTriggerRuntime.ShouldHandleEvent(GroupId, largeAreaId)).IsTrue();
    }

    /// <summary>
    /// Assigns the zone through the backing field: the public setter routes through
    /// <c>Unit.OnZoneChange</c>, which needs the ZoneManager DI singleton this test
    /// deliberately does not stand up.
    /// </summary>
    private static void SetZone(GameObject obj, uint zoneId) =>
        typeof(AAEmu.Game.Models.Game.World.Transform.Transform)
            .GetField("_zoneId", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(obj.Transform, zoneId);

    private void Place(GameObject obj, float x, float y)
    {
        obj.Transform.World.Position = new Vector3(x, y, 0f);
        var region = _world.GetRegionByPos(obj.Transform.World.Position);
        region.AddObject(obj);
        obj.Region = region;
    }
}
