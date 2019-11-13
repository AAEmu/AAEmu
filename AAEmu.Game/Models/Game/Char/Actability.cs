using System;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.Game.Models.Game.Char
{
    public class Actability
    {
        public uint Id { get; set; }
        public ActabilityTemplate Template { get; set; }
        public int Point { get; set; }
        public byte Step { get; set; }

        public Actability(ActabilityTemplate template)
        {
            Id = template.Id;
            Template = template;
        }

        public DB.Game.Actabilities ToEntity(uint ownerId)
            =>
            new DB.Game.Actabilities
            {
                Id    = this.Id    ,
                Point = this.Point ,
                Step  = this.Step  ,
                Owner = ownerId    ,
            };

        public static explicit operator Actability(Actabilities v)
            =>
            new Actability(CharacterManager.Instance.GetActability((uint)v.Id))
            {
                Id    = v.Id    ,
                Point = v.Point ,
                Step  = v.Step  ,
            };
    }
}
