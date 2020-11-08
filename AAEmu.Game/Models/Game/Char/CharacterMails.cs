using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Actions;
using System.Net.Mail;
using AAEmu.Game.Core.Managers.Id;
using SQLitePCL;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMails
    {
        public Character Self { get; set; }
        public CountUnreadMail unreadMailCount;

        public CharacterMails(Character self)
        {
            Self = self;

            unreadMailCount = new CountUnreadMail
            {
                Sent = 0,
                Received = 0,
                MiaReceived = 0,
                CommercialReceived = 0
            };
        }

        public void OpenMailbox()
        {
            var total = 0;
            foreach (var m in MailManager.Instance.GetCurrentMailList(Self))
            {
                if (m.Value.Header.SenderId == Self.Id && m.Value.Header.ReceiverId == Self.Id)
                {
                    Self.SendPacket(new SCMailListPacket(false, new MailHeader[] { m.Value.Header }));
                    total++;
                }
                else if (m.Value.Header.SenderId == Self.Id)
                {
                    Self.SendPacket(new SCMailListPacket(true, new MailHeader[] { m.Value.Header }));
                    total++;
                }
                else if (m.Value.Header.ReceiverId == Self.Id)
                {
                    Self.SendPacket(new SCMailListPacket(false, new MailHeader[] { m.Value.Header }));
                    total++;
                }
            }
            Self.SendPacket(new SCMailListEndPacket(total, 0));
        }

        public void ReadMail(bool isSent, long id)
        {
            if (MailManager.Instance._allPlayerMails.TryGetValue(id, out var mail))
            {
                if (mail.Header.Status == 0 && !isSent)
                {
                    unreadMailCount.Received -= 1;
                    mail.Header.OpenDate = DateTime.UtcNow;
                    mail.Header.Status = MailStatus.Read;
                }
                Self.SendPacket(new SCMailBodyPacket(false, isSent, mail.Body, true, unreadMailCount));
                Self.SendPacket(new SCMailStatusUpdatedPacket(isSent, id, mail.Header.Status));
                Self.SendPacket(new SCCountUnreadMailPacket(unreadMailCount));
            }
        }

        public bool SendMailToPlayer(MailType mailType, string receiverName, string title, string text, byte attachments, int money0, int money1, int money2, long extra, List<(Items.SlotType, byte)> itemSlots)
        {
            var mail = new MailPlayerToPlayer(Self,receiverName);

            mail.MailType = mailType;
            mail.Title = title;

            mail.Header.Attachments = attachments;
            mail.Header.Extra = extra;

            mail.Body.Text = text;
            mail.Body.SendDate = DateTime.UtcNow;
            mail.Body.RecvDate = DateTime.UtcNow;

            mail.AttachMoney(money0, money1, money2);

            // First verify source items, and add them to the attachments of body
            if (!mail.PrepareAttachmentItems(itemSlots))
            {
                Self.SendErrorMessage(Error.ErrorMessageType.MailInvalidItem);
                return false;
            }

            // With attachments in place, we can calculate the send fee
            var mailFee = mail.GetMailFee();
            if ((mailFee + money0) > Self.Money)
            {
                Self.SendErrorMessage(Error.ErrorMessageType.MailNotEnoughMoney);
                return false;
            }

            if (!mail.FinalizeAttachments())
                return false; // Should never fail at this point

            // Add delay if not a normal snail mail
            if (mailType == MailType.Normal)
                mail.Body.RecvDate = DateTime.UtcNow + MailManager.NormalMailDelay;

            // Send it
            if (mail.Send())
            {
                Self.SendPacket(new SCMailSentPacket(mail.Header, itemSlots.ToArray()));
                // Take the fee
                Self.SubtractMoney(SlotType.Inventory, mailFee + money0);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void GetAttached(long mailId, bool takeMoney, bool takeItems, bool takeAllSelected)
        {
            if (MailManager.Instance._allPlayerMails.TryGetValue(mailId, out var thisMail))
            {
                if (thisMail.Body.CopperCoins > 0 && takeMoney)
                {
                    Self.ChangeMoney(SlotType.Inventory, thisMail.Body.CopperCoins);
                    thisMail.Body.CopperCoins = 0;
                    thisMail.Header.Attachments -= 1;
                }

                var itemSlotList = new List<ItemIdAndLocation>();
                if (takeItems)
                {
                    var toRemove = new List<Item>();
                    foreach (var itemAttachment in thisMail.Body.Attachments)
                    {
                        if (itemAttachment.Id != 0)
                        {
                            if (Self.Inventory.Bag.FreeSlotCount > 0)
                            {
                                // TODO: Handle proper item stacking
                                Self.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Mail, itemAttachment);
                                var iial = new ItemIdAndLocation();
                                iial.Id = itemAttachment.Id;
                                iial.SlotType = itemAttachment.SlotType;
                                iial.Slot = (byte)itemAttachment.Slot;
                                itemSlotList.Add(iial);
                                thisMail.Header.Attachments -= 1;
                                toRemove.Add(itemAttachment);
                            }
                        }
                    }
                    // thisMail.Body.Attachments.Clear();
                    foreach (var ia in toRemove)
                        thisMail.Body.Attachments.Remove(ia);
                    
                }
                Self.SendPacket(new SCAttachmentTakenPacket(mailId, takeMoney, false, takeAllSelected, itemSlotList));
                Self.SendPacket(new SCMailStatusUpdatedPacket(false, mailId, MailStatus.Read));
                // TODO: if source player is online, update their mail info (sent tab)
            }
        }

        public void DeleteMail(long id, bool isSent)
        {
            if (MailManager.Instance._allPlayerMails.ContainsKey(id) && !isSent)
            {
                if (MailManager.Instance._allPlayerMails[id].Header.Attachments <= 0)
                {
                    if (MailManager.Instance._allPlayerMails[id].Header.Status == 0)
                        Self.SendPacket(new SCMailDeletedPacket(isSent, id, true, unreadMailCount));
                    else
                        Self.SendPacket(new SCMailDeletedPacket(isSent, id, false, unreadMailCount));
                    MailManager.Instance.DeleteMail(id);
                }
            }
        }

        public void ReturnMail(long id)
        {
            if (MailManager.Instance._allPlayerMails.ContainsKey(id))
            {
                var thisMail = MailManager.Instance._allPlayerMails[id];
                var itemSlots = new List<(SlotType slotType, byte slot)>();
                for (var i = 0; i < MailBody.MaxMailAttachments; i++)
                {
                    var item = ItemManager.Instance.GetItemByItemId(thisMail.Body.Attachments[i].Id);
                    if (item.SlotType == SlotType.None)
                        itemSlots.Add(((byte)0, (byte)0));
                    else
                        itemSlots.Add((item.SlotType, (byte)item.Slot));
                }

                SendMailToPlayer(thisMail.Header.Type, thisMail.Header.SenderName, thisMail.Header.Title, thisMail.Body.Text, 
                    thisMail.Header.Attachments, thisMail.Body.CopperCoins, thisMail.Body.MoneyAmount1, thisMail.Body.MoneyAmount2,
                        thisMail.Header.Extra, itemSlots);

                DeleteMail(id, false);
            }
        }
    }
}
