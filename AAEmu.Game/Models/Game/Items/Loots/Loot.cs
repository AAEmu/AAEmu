using System;

namespace AAEmu.Game.Models.Game.Items.Loots
{
    public class Loot : IComparable<Loot>
    {
        public uint Id { get; set; }
        public uint Group { get; set; }
        public uint ItemId { get; set; }
        public uint DropRate { get; set; }
        public int MinAmount { get; set; }
        public int MaxAmount { get; set; }
        public uint LootPackId { get; set; }
        public byte GradeId { get; set; }
        public bool AlwaysDrop { get; set; }

        /// <summary>
        /// To sort an array
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int CompareTo(Loot other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}