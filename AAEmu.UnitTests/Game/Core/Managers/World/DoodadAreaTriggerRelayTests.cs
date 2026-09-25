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
    private const int Radius = 25;
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
        // The spatial scan reads Unit.ModelSize, which consults the model catalog. An
        // empty catalog answers 0 for every model, which is all the range gate needs.
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
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, DoodadObjId);
        edges.TryTransition(key, entering: true);
        await Assert.That(edges.IsInside(key)).IsTrue();

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, Radius, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsFalse();
    }

    [Test]
    public async Task LeaveEdge_ForAnotherZoneDoesNotReleaseThisZonesMembership()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(OtherZoneId, UnitId, GroupId, Radius, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task Edges_AreIgnoredWithoutZoneAuthority()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, GroupId, DoodadObjId);
        edges.TryTransition(key, entering: true);
        WorldIntegration.ZoneAuthority = false;

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, Radius, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task LeaveEdge_ForAnUnknownUnitDoesNotReleaseAnything()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnknownUnitId, GroupId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnknownUnitId, GroupId, Radius, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    [Arguments(0, GroupId)]
    [Arguments(Radius, 0u)]
    [Arguments(-Radius, GroupId)]
    public async Task Edges_WithoutAUsableGroupAndRadiusAreIgnored(int radius, uint groupId)
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        var key = new AreaEdgeKey(ZoneId, UnitId, groupId, DoodadObjId);
        edges.TryTransition(key, entering: true);

        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, groupId, radius, 0, entering: false);

        await Assert.That(edges.IsInside(key)).IsTrue();
    }

    [Test]
    public async Task EnterEdge_RunsTheRealScanAndClaimsNothingWhenTheRowHasNoContent()
    {
        var edges = AreaTriggerManager.Instance.AreaEdges;
        // No DoodadManager content in this scope, so the row resolves to no template and
        // the edge must be a clean no-op rather than a throw or a false claim.
        DoodadAreaTriggerRuntime.OnZoneAreaEvent(ZoneId, UnitId, GroupId, Radius, 0, entering: true);

        await Assert.That(edges.IsInside(new AreaEdgeKey(ZoneId, UnitId, GroupId, DoodadObjId))).IsFalse();
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
