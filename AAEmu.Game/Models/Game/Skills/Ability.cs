namespace AAEmu.Game.Models.Game.Skills;

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
    //Predator = 11, // add in 3+
    //Trooper = 12,  // add in 3+
    //None = 13      // add in 3+
    Hatred = 11, // in 5.7.5.0
    Assassin = 12, // in 5.7.5.0
    Madness = 13, // in 5.7.5.0
    //Space1 = 12,
    //Space2 = 13,
    Space3 = 14,
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
    Predator = 28, // in 5.7.5.0
    Trooper = 29, // in 5.7.5.0
    None = 30 // invalid ability
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
