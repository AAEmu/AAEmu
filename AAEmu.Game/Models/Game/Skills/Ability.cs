namespace AAEmu.Game.Models.Game.Skills
{
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
        Predator = 11, // add in 3+
        Trooper = 12,  // add in 3+
        None = 13      // add in 3+
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
}
