using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using Quartz.Impl.AdoJobStore;

namespace AAEmu.Game.Models.Game.Mails
{
    public class MailBody : PacketMarshaler
    {
        public static byte MaxMailAttachments = 10;
        public long mailId { get; set; }
        public MailType Type { get; set; }
        public string ReceiverName { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public int CopperCoins { get; set; }
        public int MoneyAmount1 { get; set; }
        public int MoneyAmount2 { get; set; }
        public DateTime SendDate { get; set; }
        public DateTime RecvDate { get; set; }
        public DateTime OpenDate { get; set; }
        public List<Item> Attachments { get; set; } // TODO max length 10

        public MailBody()
        {
            Attachments = new List<Item>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(mailId);
            stream.Write((byte)Type);
            stream.Write(ReceiverName);
            stream.Write(Title);
            stream.Write(Text);
            stream.Write(CopperCoins);
            stream.Write(MoneyAmount1);
            stream.Write(MoneyAmount2);
            stream.Write(SendDate);
            stream.Write(RecvDate);
            stream.Write(OpenDate);
            for (var i = 0; i < MaxMailAttachments; i++)
            {
                if ((i >= Attachments.Count) || (Attachments[i] == null))
                    stream.Write(0);
                else
                    stream.Write(Attachments[i]);
            }

            return stream;
        }
    }
}
