using System;

namespace AAEmu.Game.Models.Game.Items
{
    public class LootGroups : IComparable<LootGroups>
    {
        public uint Id { get; set; }
        public uint PackId { get; set; }
        public int GroupNo { get; set; }
        public uint DropRate { get; set; }
        public byte ItemGradeDistributionId { get; set; }

        /*
         * To sort an array
         */
        public int CompareTo(LootGroups other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}
