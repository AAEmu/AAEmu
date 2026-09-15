using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// SpawnDoodad used to place and initialise its doodad and then Thread.Sleep(delay) on the effect
/// thread before Spawn(). The placement still happens inline; the spawn itself is now a scheduled task
/// that runs once the delay has passed.
/// </summary>
[NotInParallel]
public class SpawnDoodadSpecialEffectTests
{
    private const uint DoodadTemplateId = 995;
    private const uint DoodadObjId = 4321;
    private const uint CasterObjId = 11;
    private const uint TargetObjId = 22;

    private SingletonScope<DoodadManager> _doodads;
    private SingletonScope<WorldManager> _worlds;
    private SingletonScope<TaskManager> _tasks;
    private TaskManager _taskManager;
    private WorldInstance _world;

    [Before(Test)]
    public void Setup()
    {
        var objectIds = Mock.Of<INonUnitObjectIdManager>();
        objectIds.GetNextId().Returns(DoodadObjId);
        var doodads = new DoodadManager(objectIds.Object, Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object, Mock.Of<IFactionManager>().Object);
        SetField(doodads, "_templates", new Dictionary<uint, DoodadTemplate>
        {
            [DoodadTemplateId] = new DoodadTemplate { Id = DoodadTemplateId }
        });
        SetField(doodads, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>());
        SetField(doodads, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        _doodads = new SingletonScope<DoodadManager>(doodads);

        var worlds = new WorldManager(Mock.Of<ITickManager>().Object, Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        _worlds = new SingletonScope<WorldManager>(worlds);

        // A real, never started task manager: a scheduled spawn stays queued instead of firing.
        _taskManager = new TaskManager(Mock.Of<ITickManager>().Object);
        _tasks = new SingletonScope<TaskManager>(_taskManager);

        var template = new WorldTemplate { Id = 1, Name = "a3-spawn-doodad-test" };
        template.GeoData = new GeoDataManager(template);
        _world = new WorldInstance(template, 0, true, 1);
        // GameObject.ParentWorld/Transform.InstanceId look the instance up in WorldManager, so the test
        // world has to be registered there before anything is placed in it.
        SetField(worlds, "_worlds", new ConcurrentDictionary<uint, WorldInstance> { [_world.Id] = _world });
    }

    [After(Test)]
    public void Teardown()
    {
        _tasks.Dispose();
        _worlds.Dispose();
        _doodads.Dispose();
    }

    [Test]
    public async Task PositiveDelay_PlacesTheDoodadButDefersTheSpawn()
    {
        var started = Stopwatch.GetTimestamp();
        RunEffect(delayMilliseconds: 3000);
        var elapsed = Stopwatch.GetElapsedTime(started);

        // 3 s of delay used to be spent inside Execute.
        await Assert.That(elapsed.TotalMilliseconds).IsLessThan(500);
        await Assert.That(_world.GetAllDoodads()).IsEmpty();

        var scheduled = SingleScheduledTask();
        await Assert.That(scheduled).IsTypeOf<DeferredDoodadSpawnTask>();
        var deferred = (DeferredDoodadSpawnTask)scheduled;
        await Assert.That(deferred.Doodad.TemplateId).IsEqualTo(DoodadTemplateId);
        await Assert.That(deferred.Doodad.ObjId).IsEqualTo(DoodadObjId);
        await Assert.That(deferred.Doodad.IsDeleted).IsFalse();
        await Assert.That(scheduled.ExecuteCount).IsEqualTo(0);
        await Assert.That((scheduled.TriggerTime - DateTime.UtcNow).TotalMilliseconds).IsGreaterThan(2500);

        // Running the deferred part is what makes the doodad visible.
        deferred.Execute();
        await Assert.That(_world.GetAllDoodads().Count).IsEqualTo(1);
        await Assert.That(_world.GetDoodad(DoodadObjId)).IsSameReferenceAs(deferred.Doodad);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-5000)] // spawn_doodad carries negative delays down to -5000
    public async Task ZeroOrNegativeDelay_SpawnsInlineWithoutQueueingATask(int delayMilliseconds)
    {
        var started = Stopwatch.GetTimestamp();
        RunEffect(delayMilliseconds);
        var elapsed = Stopwatch.GetElapsedTime(started);

        await Assert.That(elapsed.TotalMilliseconds).IsLessThan(500);
        await Assert.That(_taskManager.GetQueueCount()).IsEqualTo(0);
        await Assert.That(_world.GetDoodad(DoodadObjId)).IsNotNull();
    }

    [Test]
    public async Task DeferredSpawn_OnADoodadThatWasDeletedInsideTheDelay_IsSkipped()
    {
        RunEffect(delayMilliseconds: 3000);
        var deferred = (DeferredDoodadSpawnTask)SingleScheduledTask();
        typeof(Doodad).GetField("_deleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(deferred.Doodad, true);

        Exception thrown = null;
        try
        {
            deferred.Execute();
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNull();
        await Assert.That(_world.GetAllDoodads()).IsEmpty();
    }

    private void RunEffect(int delayMilliseconds)
    {
        var caster = new BaseUnit { ObjId = CasterObjId };
        caster.ParentWorld = _world;
        var target = new BaseUnit { ObjId = TargetObjId };
        target.ParentWorld = _world;

        new SpawnDoodad().Execute(caster, null, target, new SkillCastUnitTarget(TargetObjId), null, null, null,
            DateTime.UtcNow, (int)DoodadTemplateId, delayMilliseconds, 0, 0);
    }

    private AAEmu.Game.Models.Tasks.Task SingleScheduledTask()
    {
        var queue = (ConcurrentDictionary<uint, AAEmu.Game.Models.Tasks.Task>)typeof(TaskManager)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_taskManager)!;
        return queue.Values.Single();
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }
}
