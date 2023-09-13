using System;

namespace AAEmu.Game.Models.Game.Items.Loots
{
    public class LootPackDroppingNpc : IComparable<LootPackDroppingNpc>
    {
        public uint Id { get; set; }
        public uint NpcId { get; set; }
        public uint LootPackId { get; set; }
        public bool DefaultPack { get; set; }

        /// <summary>
        /// To sort an array
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public int CompareTo(LootPackDroppingNpc other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}