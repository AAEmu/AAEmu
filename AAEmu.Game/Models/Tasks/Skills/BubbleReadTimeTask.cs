using NLog;

namespace AAEmu.Game.Models.Tasks.Skills;

/// <summary>
/// The read window of one bubble effect, held on the task scheduler instead of being slept out of the
/// effect thread. By the time this runs <c>BubbleEffect.Apply</c> has already sent the bubble packet,
/// and the client retires the bubble on its own, so it closes the window rather than undoing anything.
/// </summary>
/// <remarks>
/// The blocking sleep this replaces was added so that a cast carrying a chain of bubble effects waited
/// for each line to be read before the next one started. That pacing belongs to the effect pipeline,
/// not to a blocking effect, and this task deliberately does not re-implement it: it only marks the
/// read window as finished, off the effect thread, which is also where any real "after the bubble"
/// work would go.
/// </remarks>
public sealed class BubbleReadTimeTask(uint bubbleId, uint targetObjId, int readTimeMilliseconds) : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint BubbleId { get; } = bubbleId;

    public uint TargetObjId { get; } = targetObjId;

    public int ReadTimeMilliseconds { get; } = readTimeMilliseconds;

    public override void Execute()
    {
        Logger.Trace("BubbleEffect read window elapsed, Id {0}, ObjId {1}, {2} ms",
            BubbleId, TargetObjId, ReadTimeMilliseconds);
    }
}
