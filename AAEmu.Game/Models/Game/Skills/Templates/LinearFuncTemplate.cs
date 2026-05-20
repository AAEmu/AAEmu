namespace AAEmu.Game.Models.Game.Skills.Templates;

public class LinearFuncTemplate
{
    public uint Id { get; init; }
    public int StartValue { get; init; }
    public int EndValue { get; init; }

    /// <summary>
    /// Linear interpolation between StartValue and EndValue.
    /// ratio is expected in [0, 1] where 0 -> StartValue and 1 -> EndValue.
    /// (ratio = elapsed / duration of the source buff.)
    /// </summary>
    public double GetValue(double ratio)
    {
        ratio = Math.Clamp(ratio, 0d, 1d);
        return StartValue + (EndValue - StartValue) * ratio;
    }
}
