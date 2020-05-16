using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils;

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
            foreach (var m in MailManager.Instance.GetCurrentMailList(Self))
            {
                if (m.Value.Header.SenderId == Self.Id && m.Value.Header.ReceiverId == Self.Id)
                    Self.SendPacket(new SCMailListPacket(false, new MailHeader[] { m.Value.Header }));
                else if (m.Value.Header.SenderId == Self.Id)
                    Self.SendPacket(new SCMailListPacket(true, new MailHeader[] { m.Value.Header }));
                else if (m.Value.Header.ReceiverId == Self.Id)
                    Self.SendPacket(new SCMailListPacket(false, new MailHeader[] { m.Value.Header }));
            }
            Self.SendPacket(new SCMailListEndPacket(0, 0));
        }

        public void ReadMail(bool isSent, long id)
        {
            if (MailManager.Instance._allPlayerMails.ContainsKey(id))
            {
                if (MailManager.Instance._allPlayerMails[id].Header.Status == 0 && !isSent)
                {
                    unreadMailCount.Received -= 1;
                    MailManager.Instance._allPlayerMails[id].Header.OpenDate = DateTime.UtcNow;
                    MailManager.Instance._allPlayerMails[id].Header.Status = 1;
                }
                Self.SendPacket(new SCMailBodyPacket(false, isSent, MailManager.Instance._allPlayerMails[id].Body, true, unreadMailCount));
                Self.SendPacket(new SCMailStatusUpdatedPacket(isSent, id, MailManager.Instance._allPlayerMails[id].Header.Status));
                //Self.SendPacket(new SCCountUnreadMailPacket(unreadMailCount));
            }
        }

        public void SendMail(byte type, string receiverName, string senderName, string title, string text, byte attachments, int money0, int money1, int money2, long extra, List<(Items.SlotType, byte)> itemSlots)
        {
            var mailTemplate = new Mail();
            mailTemplate.Id = MailManager.Instance.highestMailID += 1;
            uint senderId = 0;
            uint receiverId = 0;

            mailTemplate.Header = new MailHeader()
            {
                mailId = mailTemplate.Id,
                Type = type,
                Status = (byte)0,
                Title = title,
                SenderName = senderName,
                Attachments = attachments,
                ReceiverName = receiverName,
                OpenDate = DateTime.MinValue,
                Returned = (byte)0,
                Extra = extra
            };
            if (senderName != "")
            {
                senderId = NameManager.Instance.GetCharacterId(senderName);
                mailTemplate.Header.SenderId = senderId;
            }
            if (receiverName != "")
            {
                receiverId = NameManager.Instance.GetCharacterId(receiverName);
                mailTemplate.Header.ReceiverId = receiverId;
            }

            var mailItemIds = new List<ulong>();
            foreach (var mailSlots in itemSlots)
            {
                if (mailSlots.Item1 != 0)
                {
                    var tempItem = Self.Inventory.GetItem(mailSlots.Item1, mailSlots.Item2);
                    if (tempItem.SlotType == SlotType.Inventory)
                    {
                        InventoryHelper.RemoveItemForMailing(Self,tempItem);
                        mailItemIds.Add(tempItem.Id);
                    }
                }
            }
            mailTemplate.Body = new MailBody()
            {
                mailId = mailTemplate.Id,
                Type = mailTemplate.Header.Type,
                ReceiverName = mailTemplate.Header.ReceiverName,
                Title = mailTemplate.Header.Title,
                Text = text,
                MoneyAmount1 = money0,
                MoneyAmount2 = money1,
                MoneyAmount3 = money2,
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
                OpenDate = mailTemplate.Header.OpenDate,
            };
            foreach(var iId in mailItemIds)
            {
                var i = ItemManager.Instance.GetItemByItemId(iId);
                if (i != null)
                {
                    i.OwnerId = receiverId;
                    i.SlotType = SlotType.Mail;
                    i.Slot = mailTemplate.Body.Attachments.Count;
                    mailTemplate.Body.Attachments.Add(i);
                }
            }

            MailManager.Instance._allPlayerMails.Add(MailManager.Instance.highestMailID, mailTemplate);

            Self.SendPacket(new SCMailSentPacket(mailTemplate.Header, itemSlots.ToArray()));
            var mailFee = mailTemplate.Header.Attachments * 30;
            if (mailTemplate.Header.Type == 1)
                mailFee += 10 ;
            else if (mailTemplate.Header.Type == 2)
                mailFee += 100 ;
            Self.ChangeMoney(SlotType.None, SlotType.Inventory, -mailFee);

            MailManager.Instance.NotifyNewMailByNameIfOnline(mailTemplate, receiverName);
        }

        public void GetAttached(long id, bool money, bool items, bool takeAllSelected)
        {
            if (MailManager.Instance._allPlayerMails.ContainsKey(id))
            {
                var thisMail = MailManager.Instance._allPlayerMails[id];
                if (thisMail.Body.MoneyAmount1 > 0 && money)
                {
                    Self.ChangeMoney(SlotType.None, SlotType.Inventory, thisMail.Body.MoneyAmount1);
                    thisMail.Body.MoneyAmount1 = 0;
                    thisMail.Header.Attachments -= 1;
                }
                var itemIDList = new List<ulong>();
                var itemSlotList = new List<(SlotType, byte)>();
                if (items)
                {
                    // TODO: Handle full inventory
                    foreach (var itemAttachment in thisMail.Body.Attachments)
                    {
                        if (itemAttachment.Id != 0)
                        {
                            itemSlotList.Add((itemAttachment.SlotType, (byte)itemAttachment.Slot));
                            itemAttachment.OwnerId = Self.Id;
                            itemAttachment.SlotType = SlotType.Inventory;
                            itemAttachment.Slot = -1;
                            InventoryHelper.AddItemAndUpdateClient(Self, itemAttachment);
                            thisMail.Header.Attachments -= 1;
                        }
                        else
                            itemSlotList.Add((SlotType.None, (byte)0));
                    }
                    thisMail.Body.Attachments.Clear();
                }
                Self.SendPacket(new SCAttachmentTakenPacket(id, money, false, takeAllSelected, itemIDList.ToArray(), itemSlotList.ToArray()));
                Self.SendPacket(new SCMailStatusUpdatedPacket(false, id, 1));
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

                SendMail(thisMail.Header.Type, thisMail.Header.SenderName, thisMail.Header.ReceiverName, thisMail.Header.Title, thisMail.Body.Text, 
                    thisMail.Header.Attachments, thisMail.Body.MoneyAmount1, thisMail.Body.MoneyAmount2, thisMail.Body.MoneyAmount3,
                        thisMail.Header.Extra, itemSlots);

                DeleteMail(id, false);
            }
        }
    }
}
