namespace AAEmu.Game.Models.Game.Skills;

// 10.0.2.13 skillset ids — matches compact.sqlite3 enum_abilities (0..29). The empty ability slot is
// sentinel 30 (one past the last real skillset), which the client sends for unfilled ability2/ability3.
public enum AbilityType : byte
{
    General = 0,
    Fight = 1,
    Illusion = 2,
    Adamant = 3,
    Will = 4,
    Death = 5,
    Wild = 6,
    Magic = 7,
    Vocation = 8,
    Romance = 9,
    Love = 10,
    Hatred = 11,
    Assassin = 12,
    Madness = 13,
    Pleasure = 14,
    Space4 = 15,
    Space5 = 16,
    Space6 = 17,
    Space7 = 18,
    Space8 = 19,
    Space9 = 20,
    Space10 = 21,
    Space11 = 22,
    Space12 = 23,
    Space13 = 24,
    Space14 = 25,
    Space15 = 26,
    Space16 = 27,
    Predator = 28,
    Trooper = 29,
    None = 30
}

public class Ability
{
    public AbilityType Id { get; set; }
    public byte Order { get; set; }
    public int Exp { get; set; }

    public Ability()
    {
        Order = 255;
    }

    public Ability(AbilityType id)
    {
        Id = id;
        Order = 255;
    }
}