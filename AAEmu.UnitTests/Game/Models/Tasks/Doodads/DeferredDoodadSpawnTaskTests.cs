using System.Reflection;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.UnitTests.Game.Models.Tasks.Doodads;

/// <summary>
/// The part of SpawnDoodad that runs after its delay. It makes the doodad visible only while both the
/// doodad and its world are still there: a delayed spawn can outlive the caster who asked for it.
/// </summary>
public class DeferredDoodadSpawnTaskTests
{
    [Test]
    public async Task Execute_OnALiveDoodad_SpawnsIt()
    {
        var doodad = new RecordingDoodad { ObjId = 4242, TemplateId = 995 };
        AttachWorld(doodad);

        new DeferredDoodadSpawnTask(doodad).Execute();

        await Assert.That(doodad.SpawnCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Execute_OnADoodadDeletedInsideTheDelay_LeavesItAlone()
    {
        var doodad = new RecordingDoodad { ObjId = 4242 };
        AttachWorld(doodad);
        typeof(Doodad).GetField("_deleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(doodad, true);

        new DeferredDoodadSpawnTask(doodad).Execute();

        await Assert.That(doodad.SpawnCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Execute_OnADoodadWhoseWorldIsGone_DoesNotSpawnIt()
    {
        // ParentWorld is never assigned here, so there is nothing to spawn into. Doodad.Spawn would
        // throw GameException ("Tried to spawn a object without a owning parent world") if it tried.
        var doodad = new RecordingDoodad { ObjId = 4242 };

        Exception thrown = null;
        try
        {
            new DeferredDoodadSpawnTask(doodad).Execute();
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNull();
        await Assert.That(doodad.SpawnCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Execute_OnADoodadWhoseWorldWasDisposedInsideTheDelay_DoesNotSpawnIt()
    {
        // The world is still the doodad's ParentWorld after Dispose, so the parent check passes and only
        // the disposed flag stops the spawn from adding to a world that is gone.
        var doodad = new RecordingDoodad { ObjId = 4242 };
        var world = AttachWorld(doodad);
        world.Dispose();

        await Assert.That(doodad.ParentWorld).IsSameReferenceAs(world);
        await Assert.That(world.IsDisposed).IsTrue();

        new DeferredDoodadSpawnTask(doodad).Execute();

        await Assert.That(doodad.SpawnCalls).IsEqualTo(0);
    }

    /// <summary>
    /// Assigns the world field directly: the ParentWorld property routes through
    /// <c>WorldManager.GetWorld</c>, and this test deliberately runs without a world manager.
    /// </summary>
    private static WorldInstance AttachWorld(Doodad doodad)
    {
        var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "a3-deferred-doodad-test" }, 0, true, 3);
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(doodad, world);
        return world;
    }

    private sealed class RecordingDoodad : Doodad
    {
        public int SpawnCalls { get; private set; }

        public override void Spawn() => SpawnCalls++;
    }
}
