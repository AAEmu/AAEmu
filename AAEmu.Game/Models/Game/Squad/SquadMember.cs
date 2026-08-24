namespace AAEmu.Game.Models.Game.Squad;

public class SquadMember
{
    public uint CharacterId { get; init; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Ability1 { get; set; }
    public byte Ability2 { get; set; }
    public byte Ability3 { get; set; }
    public bool IsLeader { get; set; }
    public bool Ready { get; set; }
    public bool Offline { get; set; }
    public sbyte Role { get; set; }
    public int EloRating { get; set; }
}
