using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.EventCenter;

/// <summary>
/// Re-evaluates the event-center runtime when the current/upcoming projection reaches a boundary.
/// </summary>
public sealed class EventCenterRuntimeBoundaryTask(
    EventCenterRuntimeManager runtime,
    long generation) : Task
{
    /// <summary>Identifies the schedule chain that created this task.</summary>
    public long Generation { get; } = generation;

    public override void Execute()
    {
        runtime.ExecuteBoundary(this);
    }
}
