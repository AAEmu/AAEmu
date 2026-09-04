namespace AAEmu.Game.Models.Game.StreamAoi;

/// <summary>Enter (approach) vs exit (leave) metres — sticky hysteresis.</summary>
public sealed class StreamAoiBand
{
    public float EnterMetres { get; set; }
    public float ExitMetres { get; set; }

    public float EnterSq => EnterMetres * EnterMetres;
    public float ExitSq => ExitMetres * ExitMetres;

    public StreamAoiBand Clone() => new() { EnterMetres = EnterMetres, ExitMetres = ExitMetres };
}
