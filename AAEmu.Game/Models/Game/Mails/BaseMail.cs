using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Mails
{
    public class BaseMail
    {
        private long _id;
        private MailType _mailType;
        private string _title;
        private string _receiverName;

        public long Id { get => _id; set { _id = value; Header.mailId = value; Body.mailId = value; } }
        public MailType MailType { get => _mailType; set { _mailType = value; Header.Type = value; Body.Type = value; } }
        public string Title { get => _title; set { _title = value; Header.Title = value; Body.Title = value; } }
        public string ReceiverName { get => _receiverName; set { _receiverName = value; Header.ReceiverName = value; Body.ReceiverName = value; } }

        public MailHeader Header { get; set; }
        public MailBody Body { get; set; }

        public bool IsDelivered;

        public BaseMail()
        {
            Header = new MailHeader();
            Body = new MailBody();
            IsDelivered = false;
        }

        public bool Send()
        {
            // Update Attachments just in case somebody did manual editing
            Header.Attachments = GetTotalAttachmentCount();
            return MailManager.Instance.Send(this);
        }

        public bool ReturnToSender()
        {
            // TODO
            return false;
        }

        public byte GetTotalAttachmentCount()
        {
            var res = (byte)Body.Attachments.Count;
            if (Body.CopperCoins != 0)
                res++;
            if (Body.MoneyAmount1 != 0)
                res++;
            if (Body.MoneyAmount2 != 0)
                res++;
            return res;
        }

        /// <summary>
        /// Adds money values to the body, does not actually reduce it from the player at this point
        /// </summary>
        /// <param name="copperCoinsAmount"></param>
        /// <param name="money1Amount"></param>
        /// <param name="money2Amount"></param>
        public void AttachMoney(int copperCoinsAmount, int money1Amount = 0, int money2Amount = 0)
        {
            Body.CopperCoins = copperCoinsAmount;
            Body.MoneyAmount1 = money1Amount;
            Body.MoneyAmount2 = money2Amount;
            Header.Attachments = GetTotalAttachmentCount();
        }

    }

}
