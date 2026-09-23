namespace AAEmu.Game.Models.Game.Milestones;

/// <summary>
/// The status byte the chronicle (saga book) wire carries per milestone, reusing the
/// SCChronicleInfo* family per GF-W13's mapping: 0 is the in-progress entry, non-zero is
/// completed. A milestone with no row at all is not started and is never sent — the same
/// "row absent = not held" rule the saga group records use.
/// </summary>
public enum MilestoneStatus : sbyte
{
    Active = 0,
    Complete = 1
}
