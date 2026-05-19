namespace AAEmu.Game.Models.Game.Skills.Templates;

public class LinearFuncTemplate
{
    public const uint MinAbLevel = 1u;
    public const uint MaxAbLevel = 55u;

    public uint Id { get; init; }
    public int StartValue { get; init; }
    public int EndValue { get; init; }

    /// <summary>
    /// Level-based interpolation. Returns a snapshot value computed from the caster's ability level.
    /// Kept for compatibility with PR #1433 when the dynamic_unit_modifier is NOT time-based
    /// (i.e. buff has no Duration/Tick or StartValue == EndValue).
    /// </summary>
    public int Evaluate(uint abLevel)
    {
        if (StartValue == EndValue)
            return StartValue;

        var clampedLevel = Math.Clamp(abLevel, MinAbLevel, MaxAbLevel);
        var ratio = (clampedLevel - MinAbLevel) / (double)(MaxAbLevel - MinAbLevel);
        return (int)Math.Round(StartValue + (EndValue - StartValue) * ratio);
    }

    /// <summary>
    /// Time-based interpolation along the buff duration: returns StartValue at t=0, EndValue at t=duration.
    /// Used by buffs like 2504 ("저주의 시선") and 114 ("현기증") whose linear_funcs describe an
    /// evolution over the buff's lifetime, not a level-based snapshot.
    /// </summary>
    /// <param name="elapsedMs">Time elapsed since the buff started, in milliseconds.</param>
    /// <param name="durationMs">Total buff duration, in milliseconds. If &lt;= 0, returns StartValue.</param>
    public int EvaluateTime(long elapsedMs, long durationMs)
    {
        if (StartValue == EndValue)
            return StartValue;

        if (durationMs <= 0)
            return StartValue;

        if (elapsedMs <= 0)
            return StartValue;

        if (elapsedMs >= durationMs)
            return EndValue;

        var ratio = elapsedMs / (double)durationMs;
        return (int)Math.Round(StartValue + (EndValue - StartValue) * ratio);
    }
}
