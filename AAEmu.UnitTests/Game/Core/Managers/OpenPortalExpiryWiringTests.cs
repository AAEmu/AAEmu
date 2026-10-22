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
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

using BookPortal = AAEmu.Game.Models.Game.Portal;
using GameTask = AAEmu.Game.Models.Tasks.Task;
using PortalUnit = AAEmu.Game.Models.Game.Units.Portal;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// OpenPortal must schedule <see cref="KillPortalTask"/> at the skill-description duration and
/// executing that task must remove both linked portals (and stay safe after an early delete).
/// </summary>
[NotInParallel]
public class OpenPortalExpiryWiringTests
{
    private const uint BookPortalId = 501;
    private const uint EnterNpcId = 701;
    private const uint ExitNpcId = 702;

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

        var template = new WorldTemplate { Id = 1, Name = "a3-open-portal-expiry-test" };
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
    public async Task OpenPortal_SchedulesKillPortalTask_AndExecuteRemovesBoth()
    {
        var tasks = new RecordingTaskManager();
        var (manager, owner) = CreateManager(tasks);

        manager.OpenPortal(owner,
            new SkillObjectUnk1 { Id = (int)BookPortalId, X = 10f, Y = 20f, Z = 30f }, CreateEffect());

        await Assert.That(tasks.Active).HasCount().EqualTo(1);
        await Assert.That(tasks.Active[0]).IsTypeOf<KillPortalTask>();
        await Assert.That(tasks.Delays[0]).IsEqualTo(PortalOpenDurationRules.BaseOpen);

        var entrance = manager.Created[0];
        var exit = manager.Created[1];
        await Assert.That(_world.GetNpc(entrance.ObjId)).IsNotNull();
        await Assert.That(_world.GetNpc(exit.ObjId)).IsNotNull();

        tasks.Active[0].Execute();

        await Assert.That(entrance.IsDeadOrDeleted).IsTrue();
        await Assert.That(exit.IsDeadOrDeleted).IsTrue();
        await Assert.That(_world.GetNpc(entrance.ObjId)).IsNull();
        await Assert.That(_world.GetNpc(exit.ObjId)).IsNull();
    }

    [Test]
    public async Task KillPortalTask_AfterEarlyDelete_DoesNotThrow()
    {
        var tasks = new RecordingTaskManager();
        var (manager, owner) = CreateManager(tasks);

        manager.OpenPortal(owner,
            new SkillObjectUnk1 { Id = (int)BookPortalId, X = 10f, Y = 20f, Z = 30f }, CreateEffect());
        var entrance = manager.Created[0];
        var exit = manager.Created[1];
        var pending = tasks.Active[0];

        manager.DeleteOwnerPortals(owner);
        await Assert.That(entrance.IsDeadOrDeleted).IsTrue();
        await Assert.That(exit.IsDeadOrDeleted).IsTrue();

        pending.Execute();

        await Assert.That(entrance.IsDeleted).IsTrue();
        await Assert.That(exit.IsDeleted).IsTrue();
    }

    private (WiringPortalManager Manager, Character Owner) CreateManager(ITaskManager taskManager)
    {
        var npcManager = Mock.Of<INpcManager>();
        npcManager.GetTemplate(EnterNpcId).Returns(new NpcTemplate { Id = EnterNpcId, ModelId = 1, Level = 1 });
        npcManager.GetTemplate(ExitNpcId).Returns(new NpcTemplate { Id = ExitNpcId, ModelId = 2, Level = 1 });

        var manager = new WiringPortalManager(_world, npcManager.Object, taskManager);
        var owner = new Character(new UnitCustomModelParams())
        {
            Id = 7,
            ObjId = 7,
            Name = "Owner",
            ParentWorld = _world
        };
        owner.Portals = new CharacterPortals(owner);
        owner.Portals.PrivatePortals[BookPortalId] = new BookPortal
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

    private static OpenPortalEffect CreateEffect() => new()
    {
        Id = 1,
        Distance = 3f,
        EnterPortalNpcId = EnterNpcId,
        ExitPortalNpcId = ExitNpcId
    };

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

    private sealed class RecordingTaskManager : ITaskManager
    {
        public List<GameTask> Active { get; } = [];
        public List<TimeSpan> Delays { get; } = [];

        public void Initialize() { }
        public void Start() { }
        public System.Threading.Tasks.Task StopAsync(CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;
        public bool Cancel(GameTask task) => Active.Remove(task);
        public bool CronSchedule(GameTask task, string cronExpression, TimeSpan? startDelay = null, int count = -1) =>
            false;

        public bool Schedule(GameTask task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1)
        {
            Active.Add(task);
            Delays.Add(startTime ?? TimeSpan.Zero);
            return true;
        }
    }

    private sealed class WiringPortalManager(WorldInstance world, INpcManager npcManager, ITaskManager taskManager)
        : PortalManager(Mock.Of<ILocalizationManager>().Object, Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object, npcManager, Mock.Of<IObjectIdManager>().Object, taskManager)
    {
        public List<PortalUnit> Created { get; } = [];
        private uint _nextObjId = 9100;

        protected override bool CheckCanOpenPortal(Character owner, uint targetZoneId, uint openPortalEffectId) => true;

        protected override PortalUnit MakePortal(Character owner, bool isExit, BookPortal portalInfo,
            SkillObjectUnk1 portalEffectObj, uint templateId)
        {
            var portal = new PortalUnit
            {
                ObjId = _nextObjId++,
                OwnerId = owner.Id,
                Name = portalInfo.Name,
                IsExit = isExit,
                SourcePortal = portalInfo,
                TemplateId = templateId,
                Template = new NpcTemplate { Id = templateId, ModelId = 1, Level = 1 },
                ParentWorld = world,
                Hp = 1,
                Mp = 1
            };
            world.AddObject(portal);
            Created.Add(portal);
            return portal;
        }

        protected override void DeleteLivePortal(PortalUnit portal) => portal.Delete();
    }
}
