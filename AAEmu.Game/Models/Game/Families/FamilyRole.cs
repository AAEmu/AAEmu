namespace AAEmu.Game.Models.Game.Families;

/// <summary>A family role and the maximum number of members that may hold it.</summary>
public class FamilyRole
{
    public uint Id { get; set; }
    public string IconId { get; set; }
    public string RoleName { get; set; }
    public int RoleCount { get; set; }
}
