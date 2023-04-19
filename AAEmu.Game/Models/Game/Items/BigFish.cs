using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class BigFish : Item
    {
        public override ItemDetailType DetailType => ItemDetailType.BigFish;
        public float Weight { get; set; }
        public float Length { get; set; }
        //public DateTime CreateTime { get; set; } // смотреть в Item

        public BigFish()
        {
        }

        public BigFish(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }

        public override void ReadDetails(PacketStream stream)
        {
            // 17 - 1 = 16 байт
            /*
             Length = 4
             Weight = 4
             Capture Date = 8
             */
            stream.ReadSingle();    // Weight
            stream.ReadSingle();    // Length
            stream.ReadDateTime(); // Capture Date
        }

        public override void WriteDetails(PacketStream stream)
        {
            stream.Write(Weight);     // Weight
            stream.Write(Length);     // Length
            stream.Write(CreateTime); // Capture Date
        }
    }
}
