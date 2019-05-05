using System;
namespace AAEmu.Game.Models.Game.Items
{
    public class LootPackDroppingNpc : IComparable<LootPackDroppingNpc>
    {
        public uint Id { get; set; }
        public uint NpcId { get; set; }
        public uint LootPackId { get; set; }
        public bool DefaultPack { get; set; }

        /*
         * To sort an array
         */
        public int CompareTo(LootPackDroppingNpc other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}
