using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Mails
{
    public class CommercialMail : BaseMail
    {
        private List<Item> _items;
        private uint _receiverId;
        private string _receiverName;
        private string _senderName;
        private bool _isGift;
        private bool _isRefund;
        private string _purchasedItemTitle;

        public static string InGameCashShopSenderName = ".inGameShop";

        /// <summary>
        /// Create mail for items bought from the in game cash shop. Items must be created beforehand.
        /// </summary>
        /// <param name="receiverId"></param>
        /// <param name="receiverName"></param>
        /// <param name="senderName"></param>
        /// <param name="items"></param>
        /// <param name="isGift"></param>
        /// <param name="isRefund"></param>
        /// <param name="purchasedItemTitle"></param>
        public CommercialMail(uint receiverId, string receiverName, string senderName, List<Item> items, bool isGift, bool isRefund, string purchasedItemTitle) : base()
        {
            _receiverId = receiverId;
            _receiverName = receiverName;
            _senderName = senderName;
            _items = items;
            _isGift = isGift;
            _isRefund = isRefund;
            _purchasedItemTitle = purchasedItemTitle;

            MailType = MailType.Charged;
            Header.SenderId = 0;
            Header.SenderName = InGameCashShopSenderName; // Name changes depending on type of mail
            ReceiverName = receiverName;
            Header.ReceiverId = _receiverId;

            Body.RecvDate = DateTime.UtcNow; // These mails should always be instant
        }

        /// <summary>
        /// Actually moves attached items to the mail, and generates title and body
        /// </summary>
        public void FinalizeMail()
        {
            Body.Attachments = _items;

            // If the player is exists, move it to their mail container first
            var targetCharacter = WorldManager.Instance.GetCharacter(_receiverName);
            if (targetCharacter != null)
                foreach (var item in Body.Attachments)
                    targetCharacter.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid,item);

            // Title looks like it should be the item shop entry names (in multiple language?)
            // Title = "title('Rainbow Pumpkin Taffy|Rainbow Pumpkin Taffy|Rainbow Pumpkin Taffy|彩虹南瓜糖|Радужный марципан')";
            Title = "title('" + _purchasedItemTitle + "')";
            OpenDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Always in the past

            // Not sure what all the body fields mean
            var isPresent = (_isGift && (_senderName != string.Empty)) ? "true" : "false";
            var gifterName = (_isGift ? _senderName : "");
            var giftString = (_isGift ? "1" : "0");
            var refundString = (_isRefund ? "1" : "0");
            var expireDateString = "2100,12,31,00,00,00";
            Body.Text = "body(" + isPresent + ",'" + gifterName + "','" + _purchasedItemTitle + "')" +
                        "|gift:" + giftString + ";|refund:" + refundString + ";|limit:" + expireDateString + ";";
        }
    }
}
