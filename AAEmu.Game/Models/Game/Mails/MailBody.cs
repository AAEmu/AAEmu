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
        public const byte MaxMailAttachments = 10;
        private BaseMail _baseMail;
        private string _text;
        private int _copperCoins;
        private int _billingAmount;
        private int _moneyAmount2;
        private DateTime _sendDate;
        private DateTime _recvDate;

        public long MailId { get => _baseMail.Id; }
        public MailType Type { get => _baseMail.MailType; }
        public string ReceiverName { get => _baseMail.ReceiverName; }
        public string Title { get => _baseMail.Title; }
        public string Text { get => _text; set { _text = value; _baseMail.IsDirty = true; } }
        public int CopperCoins { get => _copperCoins; set { _copperCoins = value; _baseMail.IsDirty = true; } }
        public int BillingAmount { get => _billingAmount; set { _billingAmount = value; _baseMail.IsDirty = true; } }
        public int MoneyAmount2 { get => _moneyAmount2; set { _moneyAmount2 = value; _baseMail.IsDirty = true; } }
        public DateTime SendDate { get => _sendDate; set { _sendDate = value; _baseMail.IsDirty = true; } }
        public DateTime RecvDate { get => _recvDate; set { _recvDate = value; _baseMail.IsDirty = true; } }
        public DateTime OpenDate { get => _baseMail.OpenDate; }
        public List<Item> Attachments { get; set; } // TODO max length 10

        public MailBody(BaseMail parent)
        {
            _baseMail = parent;
            Attachments = new List<Item>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(MailId);
            stream.Write((byte)Type);
            stream.Write(ReceiverName);
            stream.Write(Title);
            stream.Write(Text);
            stream.Write(CopperCoins);
            stream.Write(BillingAmount);
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
