using System;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game
{
    public class ScheduleItem : PacketMarshaler
    {
        public uint ItemTemplateId { get; set; }
        public byte Gave { get; set; }
        public int Acumulated { get; set; }
        public DateTime Updated { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(ItemTemplateId);
            stream.Write(Gave);
            stream.Write(Acumulated);
            stream.Write(Updated);
            return stream;
        }
    }
}
