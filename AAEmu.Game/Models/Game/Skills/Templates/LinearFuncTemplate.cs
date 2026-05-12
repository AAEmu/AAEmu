namespace AAEmu.Game.Models.Game.Skills.Templates;

public class LinearFuncTemplate
{
    public const uint MinAbLevel = 1u;
    public const uint MaxAbLevel = 55u;

    public uint Id { get; init; }
    public int StartValue { get; init; }
    public int EndValue { get; init; }

    public int Evaluate(uint abLevel)
    {
        if (StartValue == EndValue)
            return StartValue;

        var clampedLevel = Math.Clamp(abLevel, MinAbLevel, MaxAbLevel);
        var ratio = (clampedLevel - MinAbLevel) / (double)(MaxAbLevel - MinAbLevel);
        return (int)Math.Round(StartValue + (EndValue - StartValue) * ratio);
    }
}
