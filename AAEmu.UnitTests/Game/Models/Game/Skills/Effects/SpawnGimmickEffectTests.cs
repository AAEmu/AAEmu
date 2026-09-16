using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

using TUnit.Mocks;

using System.Collections.Concurrent;
using System.Reflection;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="SpawnGimmickEffect"/>: the branch whose <c>spawner.Spawn(0)</c> was commented out, so
/// <c>casterUnit.Gimmick</c> was never set and all 353 <c>spawn_gimmick_effects</c> rows were dead.
/// </summary>
/// <remarks>
/// There is no <c>SCGimmickJoinedPacket</c> in this tree. A created gimmick reaches a client as
/// <see cref="SCGimmicksCreatedPacket"/>, which is what <c>Gimmick.AddVisibleObject</c> sends and what the
/// 10.0.2.13 client registers at <c>SCOffsets.SCGimmicksCreatedPacket</c> (0x181); the acceptance asks for a
/// "SCGimmickJoinedPacket-shaped result", and that packet's per-gimmick body is <c>Gimmick.Write</c>, so the
/// test writes the gimmick into a real <see cref="SCGimmicksCreatedPacket"/> and reads the body back.
/// </remarks>
[NotInParallel]
public class SpawnGimmickEffectTests
{
    private const uint GimmickTemplateId = 4242;
    private const uint CasterObjId = 11;
    private const uint ObjectId = 101_000; // NonUnitObjectIdManager.FirstId
    private const uint GimmickId = 1; // GimmickIdManager.FirstId

    private SingletonScope<WorldManager> _worlds;
    private SingletonScope<GimmickGameData> _gimmickData;
    private WorldInstance _world;

    [Before(Test)]
    public void Setup()
    {
        // Both allocators are plain statics with a private `_instance`, and both are usable without a
        // database: their ObjTables are empty, so Initialize seeds an empty used-set.
        var objectIds = new NonUnitObjectIdManager();
        var gimmickIds = new GimmickIdManager();
        if (!objectIds.Initialize() || !gimmickIds.Initialize())
            throw new InvalidOperationException("the id allocators could not be primed for the test");
        SetInstance(objectIds);
        SetInstance(gimmickIds);

        var gimmickData = new GimmickGameData();
        SetField(gimmickData, "_templates", new Dictionary<uint, GimmickTemplate>
        {
            [GimmickTemplateId] = new GimmickTemplate
            {
                Id = GimmickTemplateId,
                ModelPath = "gameobjects/test/test_gimmick.ddf"
            }
        });
        _gimmickData = new SingletonScope<GimmickGameData>(gimmickData);

        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        _worlds = new SingletonScope<WorldManager>(worlds);

        var template = new WorldTemplate { Id = 1, Name = "a4-spawn-gimmick-test" };
        template.GeoData = new GeoDataManager(template);
        _world = new WorldInstance(template, 0, true, 1);
        _world.GimmickManager = new GimmickManager(_world);
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
    }

    [After(Test)]
    public void Teardown()
    {
        ClearInstance<NonUnitObjectIdManager>();
        ClearInstance<GimmickIdManager>();
        _worlds.Dispose();
        _gimmickData.Dispose();
    }

    [Test]
    public async Task ASpawnGimmickSkill_CreatesTheGimmickAndHangsItOnTheCaster()
    {
        var caster = CreateCaster();

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = GimmickTemplateId, Scale = 1.5f });

        await Assert.That(caster.Gimmick).IsNotNull();
        await Assert.That(caster.Gimmick.TemplateId).IsEqualTo(GimmickTemplateId);
        await Assert.That(_world.GimmickManager._activeGimmicks).ContainsKey(caster.Gimmick.ObjId);
    }

    [Test]
    public async Task TheCreatedGimmick_WritesASCGimmicksCreatedPacketBody()
    {
        var caster = CreateCaster();

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = GimmickTemplateId, Scale = 1.5f });

        var gimmick = caster.Gimmick;
        await Assert.That(gimmick).IsNotNull();

        // The packet the client registers for a created gimmick, written and read back the way it goes out.
        var spawnData = gimmick.ToSpawnData();
        var stream = new PacketStream();
        spawnData.Write(stream);
        var body = stream.GetBytes();

        await Assert.That(body).IsNotNull();
        await Assert.That(body.Length).IsGreaterThanOrEqualTo(GimmickSpawnData.MinimumSerializedLength);
        await Assert.That(spawnData.Id).IsEqualTo(gimmick.ObjId);
        await Assert.That(spawnData.Type).IsEqualTo(GimmickTemplateId);
        await Assert.That(spawnData.SpawnerUnitId).IsEqualTo(CasterObjId);

        // And the same record inside the packet the client actually receives.
        var packetStream = new PacketStream();
        new SCGimmicksCreatedPacket([gimmick]).Write(packetStream);
        await Assert.That(packetStream.GetBytes()).IsNotNull();
    }

    [Test]
    public async Task TheGimmickIsPlacedAtTheCastersPosition()
    {
        var caster = CreateCaster();
        caster.Transform.Local.SetPosition(1234.5f, 5678.25f, 42f, 0f, 0f, 0f);

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = GimmickTemplateId });

        await Assert.That(caster.Gimmick).IsNotNull();
        await Assert.That(caster.Gimmick.Transform.World.Position.X).IsEqualTo(1234.5f);
        await Assert.That(caster.Gimmick.Transform.World.Position.Y).IsEqualTo(5678.25f);
    }

    [Test]
    public async Task AnUnknownTemplate_LeavesTheCasterWithoutAGimmick()
    {
        var caster = CreateCaster();

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = 99999, Scale = 1f });

        await Assert.That(caster.Gimmick).IsNull();
        await Assert.That(_world.GimmickManager._activeGimmicks).IsEmpty();
    }

    [Test]
    public async Task ACasterWithNoWorld_IsRefused()
    {
        var caster = new Npc { ObjId = CasterObjId, Template = new NpcTemplate() };

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = GimmickTemplateId });

        await Assert.That(caster.Gimmick).IsNull();
    }

    [Test]
    public async Task TheScaleIsCarriedOntoTheGimmick()
    {
        var caster = CreateCaster();

        Run(caster, new SpawnGimmickEffect { Id = 1, GimmickId = GimmickTemplateId, Scale = 2.5f });

        await Assert.That(caster.Gimmick).IsNotNull();
        await Assert.That(caster.Gimmick.Scale).IsEqualTo(2.5f);
    }

    private Npc CreateCaster()
    {
        var caster = new Npc { ObjId = CasterObjId, Template = new NpcTemplate() };
        caster.ParentWorld = _world;
        caster.Transform.Local.SetPosition(1000f, 2000f, 30f, 0f, 0f, 0f);
        return caster;
    }

    private static void Run(Npc caster, SpawnGimmickEffect effect)
    {
        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            caster,
            new SkillCastUnitTarget(caster.ObjId),
            new CastSkill(17000, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }

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
