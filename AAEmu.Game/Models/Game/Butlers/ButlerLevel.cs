namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Static row from <c>butler_levels</c>.</summary>
public class ButlerLevel
{
    public uint Id { get; set; }
    public uint ButlerId { get; set; }
    public uint Level { get; set; }
    public uint MaxLaborPower { get; set; }
    public uint MaxStatPoint { get; set; }
    public long TotalExp { get; set; }
    public string? EffectDesc { get; set; }
    public uint TotalGardenCount { get; set; }
    public uint ButlerHarvestGradeId { get; set; }
}
