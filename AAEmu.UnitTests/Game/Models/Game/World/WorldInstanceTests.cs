using System.Runtime.CompilerServices;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class WorldInstanceTests
{
    private static WorldTemplate CreateTemplate() => new()
    {
        CellX = 1,
        CellY = 1,
        Cells = new WorldCell[0, 0],
        HousingZones = [],
        Id = 0,
        Name = "test_world",
        OceanLevel = 100f,
        SubZones = [],
        XmlWorld = new XmlWorld { Zones = [] },
        XmlWorldZones = [],
        ZoneKeyByRegions = new uint[1, 1],
        ZoneKeys = [0]
    };

    /// <summary>
    /// Creates a WorldInstance that becomes unreachable immediately, without disposing it
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropUndisposedInstance()
    {
        _ = new WorldInstance(CreateTemplate(), 0, false, 123u);
    }

    [Test]
    public async Task WorldInstance_HasNoFinalizer()
    {
        // A finalizer here would run on the finalizer thread and touch WorldIdManager.Instance, which is not
        // safe during collection; any exception thrown there is unhandled and takes the whole process down.
        var finalizer = typeof(WorldInstance).GetMethod(
            "Finalize",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        await Assert.That(finalizer?.DeclaringType).IsNotEqualTo(typeof(WorldInstance));
    }

    [Test]
    public void CollectingUndisposedInstance_DoesNotCrashTheProcess()
    {
        // Regression: an undisposed instance used to release its Id from its finalizer against a
        // WorldIdManager singleton that was never initialized, throwing a NullReferenceException on the
        // finalizer thread and killing the process before any test results were reported.
        DropUndisposedInstance();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Test]
    public void Dispose_WithUninitializedIdManager_DoesNotThrow()
    {
        var world = new WorldInstance(CreateTemplate(), 0, false, 123u);

        world.Dispose();
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Several shutdown paths may dispose the same world; releasing its Id twice would hand a live Id back
        // to the pool, so the second call has to be a no-op.
        var world = new WorldInstance(CreateTemplate(), 0, false, 123u);

        world.Dispose();
        world.Dispose();
    }

    [Test]
    public async Task ReleaseId_OnUninitializedManager_DoesNotThrow()
    {
        var manager = new WorldIdManager();

        // Never Initialize()d, so there is no backing BitSet to clear
        manager.ReleaseId(123u);

        await Assert.That(true).IsTrue();
    }
}
