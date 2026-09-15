namespace AAEmu.Game.Models.Game.Families;

/// <summary>A family level and its cumulative experience threshold.</summary>
public class FamilyLevel
{
    public uint Id { get; set; }
    public uint Level { get; set; }
    public string GradeName { get; set; }
    public uint Exp { get; set; }
    public uint BuffId { get; set; }
}
