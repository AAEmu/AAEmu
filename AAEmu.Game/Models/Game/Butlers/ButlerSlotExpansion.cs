namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Static row from either butler slot-expansion table.</summary>
public class ButlerSlotExpansion
{
    public uint Id { get; set; }
    public uint ButlerId { get; set; }
    public uint Level { get; set; }
    public uint TotalExpandSlotCount { get; set; }
    public uint RequireItemId { get; set; }
    public uint RequireItemCount { get; set; }
}
