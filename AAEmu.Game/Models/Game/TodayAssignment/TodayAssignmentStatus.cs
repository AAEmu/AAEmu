namespace AAEmu.Game.Models.Game.TodayAssignment;

/// <summary>
/// Daily schedule slot status: Locked → Ready → Progress → Done.
/// </summary>
public enum TodayAssignmentStatus : sbyte
{
    Locked = 0,
    Ready = 1,
    Progress = 2,
    Done = 3
}
