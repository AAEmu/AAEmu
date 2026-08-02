namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// </summary>
public static class ItemTaskListLimits
{
    public const int Tasks = 30;
    public const int ForceRemoves = 30;

    public static void Validate(IReadOnlyCollection<ItemTask> tasks, IReadOnlyCollection<ulong> forceRemoves)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(forceRemoves);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tasks.Count, Tasks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(forceRemoves.Count, ForceRemoves);
    }
}
