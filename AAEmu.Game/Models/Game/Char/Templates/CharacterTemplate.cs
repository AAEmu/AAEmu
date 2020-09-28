using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Char.Templates
{
    public class CharacterTemplate
    {
        public Race Race { get; set; }
        public Gender Gender { get; set; }
        public uint ModelId { get; set; }
        public uint ZoneId { get; set; }
        public uint FactionId { get; set; }
        public uint ReturnDictrictId { get; set; }
        public uint ResurrectionDictrictId { get; set; }
        public Point Position { get; set; }
        public uint[] Items { get; set; }
        public List<uint> Buffs { get; set; }
        public byte NumInventorySlot { get; set; }
        public short NumBankSlot { get; set; }

        public CharacterTemplate()
        {
            Position = new Point();
            Items = new uint[7];
            Buffs = new List<uint>();
        }
    }
}
