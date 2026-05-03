namespace AAEmu.Game.Models.Game.Skills.Templates;

public class LinearFuncTemplate
{
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
        var clampedLevel = Math.Clamp(abLevel, 1u, 55u);
        var ratio = (clampedLevel - 1d) / 54d;
        return (int)Math.Round(StartValue + (EndValue - StartValue) * ratio);
    }
}
