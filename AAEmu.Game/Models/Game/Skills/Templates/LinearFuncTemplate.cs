namespace AAEmu.Game.Models.Game.Skills.Templates;

public class LinearFuncTemplate
{
    /// <summary>
    /// Minimum ability level used for linear interpolation.
    /// Matches the lower bound of the ArcheAge ability-level range used by buff AbLevel.
    /// </summary>
    public const uint MinAbLevel = 1u;

    /// <summary>
    /// Maximum ability level used for linear interpolation.
    /// Matches the upper bound of the ArcheAge ability-level range used by buff AbLevel
    /// (same cap as <c>FeatureSet</c>'s ability cap). If the cap ever changes per game
    /// version, update this constant (or wire it to <c>FeatureSet</c>) so interpolation
    /// stays correct.
    /// </summary>
    public const uint MaxAbLevel = 55u;

    public uint Id { get; init; }
    public int StartValue { get; init; }
    public int EndValue { get; init; }

    public int Evaluate(uint abLevel)
    {
        if (StartValue == EndValue)
            return StartValue;

        // Most dynamic_unit_modifiers currently point to constant LinearFunc rows.
        // For non-constant rows, keep a safe linear interpolation across the
        // ArcheAge ability-level range used by buff AbLevel.
        var clampedLevel = Math.Clamp(abLevel, MinAbLevel, MaxAbLevel);
        var ratio = (clampedLevel - MinAbLevel) / (double)(MaxAbLevel - MinAbLevel);
        return (int)Math.Round(StartValue + (EndValue - StartValue) * ratio);
    }
}
