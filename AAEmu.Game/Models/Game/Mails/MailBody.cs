using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails
{
    public class MailBody : PacketMarshaler
    {
        public long Id { get; set; }
        public byte Type { get; set; }
        public string ReceiverName { get; set; }
        public string Text { get; set; }
        public int MoneyAmount1 { get; set; }
        public int MoneyAmount2 { get; set; }
        public int MoneyAmount3 { get; set; }
        public DateTime SendDate { get; set; }
        public DateTime RecvDate { get; set; }
        public DateTime OpenDate { get; set; }
        public Item[] Items { get; set; } // TODO max length 10

        public override PacketStream Write(PacketStream stream)
        {
            
            return stream;
        }
    }
}
