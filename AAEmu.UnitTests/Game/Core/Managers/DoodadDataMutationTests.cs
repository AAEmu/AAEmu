using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public sealed class DoodadDataMutationTests
{
    private const uint OwnerId = 7001;
    private const uint PlayerObjId = 7002;
    private const uint CofferObjId = 7003;
    private const uint SyntheticWorldId = 7004;
    private const uint SyntheticZoneKey = 7005;
    private const uint CofferTemplateId = 7006;
    private const uint CofferFuncId = 7007;
    private const uint CofferGroupId = 7008;

    private IDisposable _worldScope;
    private SingletonScope<DoodadManager> _doodadScope;
    private WorldInstance _world;

    [Before(Test)]
    public void Setup()
    {
        _worldScope = TestDungeonWorld.InstallWorldManager();
        _doodadScope = new SingletonScope<DoodadManager>(CreateDoodadManager());
        _world = CreateWorld();
        var worlds = (ConcurrentDictionary<uint, WorldInstance>)typeof(WorldManager)
            .GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(WorldManager.Instance)!;
        worlds[_world.Id] = _world;
    }

    [After(Test)]
    public void Teardown()
    {
        _world?.Dispose();
        _doodadScope.Dispose();
        _worldScope.Dispose();
    }

    [Test]
    public async Task ChangeDoodadData_MutatesVisibleOpenedAoiCofferAndBroadcasts()
    {
        var fixture = CreateFixture();
        var target = (int)HousingPermission.Public;

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, target);

        await Assert.That(changed).IsTrue();
        await Assert.That(fixture.Coffer.Data).IsEqualTo(target);
        await Assert.That(fixture.Sent.Count).IsEqualTo(1);

        var (opcode, body) = SentPacket.Read(fixture.Sent[0]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCDoodadChangedPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadBc()).IsEqualTo(CofferObjId);
        await Assert.That(stream.ReadInt32()).IsEqualTo(target);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_RejectsInvisibleDoodadWithoutMutation()
    {
        var fixture = CreateFixture();
        fixture.Coffer.IsVisible = false;

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_RejectsDoodadOutsideAoiWithoutMutation()
    {
        var fixture = CreateFixture();
        var region = fixture.Coffer.Region!;
        region.RemoveObject(fixture.Coffer);
        fixture.Coffer.Region = null;

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_RejectsUnopenedCofferWithoutMutation()
    {
        var fixture = CreateFixture();
        fixture.Coffer.OpenedBy = null;

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_RejectsNonInteractableCofferWithoutMutation()
    {
        var fixture = CreateFixture(nonInteractive: true);

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_RejectsMissingCofferPermissionTemplate()
    {
        var fixture = CreateFixture();
        SetCofferTemplate(present: false);

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_PersistenceFailureRollsBackAndDoesNotBroadcast()
    {
        var fixture = CreateFixture();
        fixture.Coffer.PersistDataForTest = () => false;

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeDoodadData_PersistenceExceptionRollsBackAndDoesNotBroadcast()
    {
        var fixture = CreateFixture();
        fixture.Coffer.PersistDataForTest = () => throw new InvalidOperationException("synthetic persistence failure");

        var changed = DoodadManager.ChangeDoodadData(fixture.Player, fixture.Coffer, (int)HousingPermission.Public);

        await Assert.That(changed).IsFalse();
        await Assert.That(fixture.Coffer.Data).IsEqualTo((int)HousingPermission.Private);
        await Assert.That(fixture.Sent.Count).IsEqualTo(0);
    }

    private Fixture CreateFixture(bool nonInteractive = false)
    {
        var sent = new List<byte[]>();
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => sent.Add(bytes));

        var player = new Character(new UnitCustomModelParams())
        {
            Id = OwnerId,
            ObjId = PlayerObjId,
            AccountId = 8001,
            Name = "SyntheticPlayer"
        };
        var connection = new GameConnection(session.Object) { ActiveChar = player };
        player.Connection = connection;
        AddToAoi(player, _world);

        DoodadCoffer coffer = nonInteractive ? new NonInteractiveCoffer() : new DoodadCoffer();
        coffer.ObjId = CofferObjId;
        coffer.OwnerId = OwnerId;
        coffer.Template = new DoodadCofferTemplate { Id = CofferTemplateId, Capacity = 8 };
        coffer.SetData((int)HousingPermission.Private);
        AddToAoi(coffer, _world);
        coffer.OpenedBy = player;
        SetCofferPermission(coffer, DoodadFuncPermission.Owner);
        return new Fixture(player, coffer, sent);
    }

    private static void AddToAoi(GameObject gameObject, WorldInstance world)
    {
        gameObject.ParentWorld = world;
        gameObject.Transform.Local.SetPosition(100f, 100f, 0f);
        gameObject.Transform.KeepZoneQuietly(SyntheticZoneKey);
        var region = world.Regions[0, 0];
        region.AddObject(gameObject);
        gameObject.Region = region;
        gameObject.IsVisible = true;
    }

    private static WorldInstance CreateWorld()
    {
        var size = WorldManager.SECTORS_PER_CELL;
        var zoneMap = new uint[size, size];
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
            zoneMap[x, y] = SyntheticZoneKey;

        var template = new WorldTemplate
        {
            Id = SyntheticWorldId,
            Name = "synthetic_e10_world",
            CellX = 1,
            CellY = 1,
            ZoneKeys = [SyntheticZoneKey],
            ZoneKeyByRegions = zoneMap
        };
        var world = new WorldInstance(template, 0, true, SyntheticWorldId);
        world.Regions = new Region[size, size];
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
            world.Regions[x, y] = new Region(world, x, y, SyntheticZoneKey);
        return world;
    }

    private static DoodadManager CreateDoodadManager()
    {
        var manager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);
        SetField(manager, "_funcTemplates", new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            [nameof(DoodadFuncCofferPerm)] = new()
            {
                [CofferFuncId] = new DoodadFuncCofferPerm { Id = CofferFuncId }
            }
        });
        return manager;
    }

    private static void SetCofferTemplate(bool present)
    {
        var templates = present
            ? new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
            {
                [nameof(DoodadFuncCofferPerm)] = new()
                {
                    [CofferFuncId] = new DoodadFuncCofferPerm { Id = CofferFuncId }
                }
            }
            : new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
        SetField(DoodadManager.Instance, "_funcTemplates", templates);
    }

    private static void SetCofferPermission(Doodad coffer, DoodadFuncPermission permission)
    {
        var field = typeof(Doodad).GetField("<CurrentFuncs>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<DoodadFunc> functions =
        [
            new DoodadFunc
            {
                GroupId = CofferGroupId,
                FuncId = CofferFuncId,
                FuncType = nameof(DoodadFuncCofferPerm),
                PermId = (uint)permission,
                NextPhase = -1
            }
        ];
        field.SetValue(coffer, functions);
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    private sealed class NonInteractiveCoffer : DoodadCoffer
    {
        public override bool AllowedToInteract(Character character) => false;
    }

    private sealed record Fixture(Character Player, DoodadCoffer Coffer, List<byte[]> Sent);
}
