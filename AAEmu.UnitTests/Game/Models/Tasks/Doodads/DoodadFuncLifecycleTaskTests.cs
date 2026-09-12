using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.UnitTests.Game.Models.Tasks.Doodads;

[NotInParallel]
public sealed class DoodadFuncLifecycleTaskTests
{
    private FieldInfo _singletonField;
    private object _previousManager;

    [Before(Test)]
    public void Setup()
    {
        _singletonField = typeof(Singleton<DoodadManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousManager = _singletonField.GetValue(null);

        var manager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);
        SetField(manager, "_funcsByGroups", new Dictionary<uint, List<DoodadFunc>>());
        SetField(manager, "_phaseFuncs", new Dictionary<uint, List<DoodadPhaseFunc>>());
        _singletonField.SetValue(null, manager);
    }

    [After(Test)]
    public void Teardown()
    {
        _singletonField.SetValue(null, _previousManager);
    }

    [Test]
    public async Task SheepPen_OldTimerCannotOverrideFedPhase()
    {
        var owner = new Doodad();
        SetPhase(owner, 22219);
        var oldHungryTimer = new DoodadFuncTimerTask(null, owner, 0, 22220);

        // Feeding changes 22219 -> 22222 and installs that phase's timer before the old task runs.
        SetPhase(owner, 22222);
        var fedPhaseTimer = new DoodadFuncTimerTask(null, owner, 0, 22219);
        owner.FuncTask = fedPhaseTimer;

        oldHungryTimer.Execute();

        await Assert.That(owner.FuncGroupId).IsEqualTo(22222u);
        await Assert.That(owner.FuncTask).IsSameReferenceAs(fedPhaseTimer);
    }

    [Test]
    public async Task CurrentLambGrowthTask_TransitionsOnlyOnce()
    {
        var owner = new Doodad();
        SetPhase(owner, 5806);
        owner.SetScale(0.5f);
        var growth = new DoodadFuncGrowthTask(null, owner, 0, 5807, 1.25f);
        owner.FuncTask = growth;

        growth.Execute();
        var completedPhaseTime = owner.PhaseTime;
        growth.Execute();

        await Assert.That(owner.Scale).IsEqualTo(1.25f);
        await Assert.That(owner.FuncTask).IsNull();
        await Assert.That(owner.FuncGroupId).IsEqualTo(5807u);
        await Assert.That(owner.PhaseTime).IsEqualTo(completedPhaseTime);
    }

    [Test]
    public async Task DeletedOrDespawningDoodad_RejectsCurrentLifecycleTask()
    {
        var deleted = new Doodad();
        SetPhase(deleted, 5806);
        SetField(deleted, "_deleted", true);
        var deletedGrowth = new DoodadFuncGrowthTask(null, deleted, 0, 5807, 1.25f);
        deleted.FuncTask = deletedGrowth;

        var despawning = new Doodad { Despawn = DateTime.UtcNow.AddSeconds(1) };
        SetPhase(despawning, 22219);
        var despawningTimer = new DoodadFuncTimerTask(null, despawning, 0, 22220);
        despawning.FuncTask = despawningTimer;

        deletedGrowth.Execute();
        despawningTimer.Execute();

        await Assert.That(deleted.FuncGroupId).IsEqualTo(5806u);
        await Assert.That(deleted.Scale).IsEqualTo(1f);
        await Assert.That(despawning.FuncGroupId).IsEqualTo(22219u);
        await Assert.That(deleted.FuncTask).IsSameReferenceAs(deletedGrowth);
        await Assert.That(despawning.FuncTask).IsSameReferenceAs(despawningTimer);
    }

    private static void SetPhase(Doodad owner, uint phase)
    {
        SetField(owner, "_funcGroupId", phase);
    }

    private static void SetField(object owner, string name, object value)
    {
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(owner, value);
    }
}
