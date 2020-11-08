using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Mails
{

    public class MailPlayerToPlayer : BaseMail
    {
        private Character _sender;

        public MailPlayerToPlayer(Character sender, string receiverPlayerName) : base()
        {
            _sender = sender;
            Header.SenderId = sender.Id;
            Header.SenderName = sender.Name;
            Header.ReceiverId = NameManager.Instance.GetCharacterId(receiverPlayerName);
            ReceiverName = receiverPlayerName;
        }

        public int GetMailFee()
        {
            // Calculate mail fee
            var mailFee = 0;
            var attachmentCost = 0;
            var attachmentCountForFee = Body.Attachments.Count;

            if (Body.CopperCoins > 0)
                attachmentCountForFee++;

            if (MailType == MailType.Normal)
            {
                mailFee += MailManager.CostNormal;
                attachmentCost = MailManager.CostNormalAttachment;
            }
            else if (MailType == MailType.Express)
            {
                mailFee += MailManager.CostExpress;
                attachmentCost = MailManager.CostExpressAttachment;
            }
            // If Invalid mail type, assume zero cost

            // Add cost based on attachments past the first one
            if (attachmentCountForFee > MailManager.CostFreeAttachmentCount)
                mailFee += (attachmentCountForFee - MailManager.CostFreeAttachmentCount) * attachmentCost;

            return mailFee;
        }

        /// <summary>
        /// Provide a list of item slots to take attachments from, does not remove items from inventory at this point, use FinalizeAttachents when ready
        /// </summary>
        /// <param name="itemSlotsForMail"></param>
        /// <returns></returns>
        public bool PrepareAttachmentItems(List<(SlotType, byte)> itemSlotsForMail)
        {
            Body.Attachments.Clear();
            foreach (var mailSlots in itemSlotsForMail)
            {
                if (mailSlots.Item1 != 0)
                {
                    var tempItem = _sender.Inventory.GetItem(mailSlots.Item1, mailSlots.Item2);
                    if ((tempItem == null) || (tempItem.SlotType != SlotType.Inventory))
                    {
                        // Attchment Items do not match player inventory, abort
                        return false;
                    }
                    Body.Attachments.Add(tempItem);
                }
            }
            Header.Attachments = GetTotalAttachmentCount();
            return true;
        }

        /// <summary>
        /// Takes all item attachments from sender's inventory and moves them to the mail container for sending
        /// </summary>
        /// <returns></returns>
        public bool FinalizeAttachments()
        {
            for (var i = 0; i < Body.Attachments.Count; i++)
            {
                var tempItem = Body.Attachments[i];
                // Move Item to sender's Mail ItemContainer, technically speaking this can never fail
                if (_sender.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid, tempItem))
                {
                    _sender.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Mail, new List<ItemTask>() { new ItemRemove(tempItem) }, new List<ulong>()));
                    // Technically not needed, I just want to sync it up
                    tempItem.SlotType = SlotType.Mail;
                    tempItem.Slot = i;
                    // tempItem.OwnerId = mailTemplate.Header.ReceiverId;
                }
                else
                {
                    // Should never be able to fail, abort
                    return false;
                }
            }
            return true;
        }
    }
}
