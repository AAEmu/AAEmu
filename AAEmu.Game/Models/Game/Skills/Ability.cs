using System;
using AAEmu.DB.Game;

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
        None = 11
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


        public DB.Game.Abilities ToEntity(uint ownerId)
            =>
            new DB.Game.Abilities()
            {
                Exp   =             this.Exp ,
                Id    = (byte)(int) this.Id  ,
                Owner =             ownerId  ,
            };

        public static explicit operator Ability(Abilities v)
            =>
            new Ability()
            {
                Id  = (AbilityType) v.Id  ,
                Exp =               v.Exp ,
            };
    }
}
