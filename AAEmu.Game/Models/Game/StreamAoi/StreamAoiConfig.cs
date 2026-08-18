namespace AAEmu.Game.Models.Game.StreamAoi;

/// <summary>Bind from <c>Configurations/StreamAoi.json</c> under <c>StreamAoi</c>.</summary>
public sealed class StreamAoiConfig
{
    public StreamAoiBand Ambient { get; set; } = new() { EnterMetres = 105f, ExitMetres = 110f };
    public StreamAoiBand Large { get; set; } = new() { EnterMetres = 225f, ExitMetres = 248f };
    public StreamAoiBand Ship { get; set; } = new() { EnterMetres = 225f, ExitMetres = 248f };
    public StreamAoiBand Event { get; set; } = new() { EnterMetres = 700f, ExitMetres = 700f };

    /// <summary>World Kraken / Leviathan templates measured on EU (optional extras).</summary>
    public List<uint> LargeNpcTemplateIds { get; set; } = [7607, 14915];

    /// <summary>compact <c>models.id</c> for those bosses (Kraken 897, Leviathan 530).</summary>
    public List<uint> LargeModelIds { get; set; } = [897, 530];
}
