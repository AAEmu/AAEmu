namespace AAEmu.Game.Models.Game.Families;

/// <summary>A paid family member-cap expansion.</summary>
public class FamilyMemberLimit
{
    public uint Id { get; set; }
    public int Count { get; set; }
    public uint ItemId { get; set; }
    public int ItemCount { get; set; }
}
