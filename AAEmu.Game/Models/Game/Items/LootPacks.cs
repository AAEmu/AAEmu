using System;
namespace AAEmu.Game.Models.Game.Items
{
    public class LootPacks : IComparable<LootPacks>
    {
        public uint Id { get; set; }
        public int Group { get; set; }
        public uint ItemId { get; set; }
        public uint DropRate { get; set; }
        public int MinAmount { get; set; }
        public int MaxAmount { get; set; }
        public uint LootPackId { get; set; }
        public byte GradeId { get; set; }
        public bool AlwaysDrop { get; set; }

        /*
         * To sort an array
         */
        public int CompareTo(LootPacks other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}
