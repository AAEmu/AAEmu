namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Verified permanent-data keys and lookup rules for farmhand progression.</summary>
public static class ButlerProgression
{
    // Client farmhand state uses permanent data key 1 for cumulative experience.
    public const sbyte CumulativeExperiencePermanentDataKey = 1;

    // The 10.0.2.13 client and the official 2022-11-17 Farmhand update expose level 40 as the usable cap.
    // butler_levels.level 41 supplies the following experience threshold; it is not a selectable Farmhand level.
    public const uint MaximumUsableLevel = 40;

    /// <summary>
    /// Applies a positive Farmhand experience grant without deciding how cumulative experience maps to the
    /// client-visible level cap. The caller keeps values above the level-41 threshold as durable progress.
    /// </summary>
    public static bool TryAddExperience(ulong current, int value, out ulong updated)
    {
        updated = current;
        if (value <= 0)
            return false;

        try
        {
            updated = checked(current + (uint)value);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
