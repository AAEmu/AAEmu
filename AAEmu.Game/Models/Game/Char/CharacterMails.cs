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
            foreach (var m in MailManager.Instance.GetCurrentMailList(Self, false))
            {
                if (m.Value.Item1.SenderName == Self.Name && m.Value.Item1.ReceiverName == Self.Name)
                    Self.SendPacket(new SCMailListPacket(false, new Mail[] { m.Value.Item1 }));
                else if (m.Value.Item1.SenderName == Self.Name)
                    Self.SendPacket(new SCMailListPacket(true, new Mail[] { m.Value.Item1 }));
                else if (m.Value.Item1.ReceiverName == Self.Name)
                    Self.SendPacket(new SCMailListPacket(false, new Mail[] { m.Value.Item1 }));
            }
            Self.SendPacket(new SCMailListEndPacket(0, 0));
        }

        public void ReadMail(bool isSent, long id)
        {
            if (MailManager.Instance.allPlayerMails.ContainsKey(id))
            {
                if (MailManager.Instance.allPlayerMails[id].Item1.Status == 0 && !isSent)
                {
                    unreadMailCount.Received -= 1;
                    MailManager.Instance.allPlayerMails[id].Item1.OpenDate = DateTime.UtcNow;
                    MailManager.Instance.allPlayerMails[id].Item1.Status = 1;
                }
                Self.SendPacket(new SCMailBodyPacket(false, isSent, MailManager.Instance.allPlayerMails[id].Item2, true, unreadMailCount));
                Self.SendPacket(new SCMailStatusUpdatedPacket(isSent, id, MailManager.Instance.allPlayerMails[id].Item1.Status));
                //Self.SendPacket(new SCCountUnreadMailPacket(unreadMailCount));
            }
        }

        public void SendMail(byte type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<(Items.SlotType, byte)> itemSlots)
        {
            var mailTemplate = new Mail()
            {
                Id = MailManager.Instance.highestMailID += 1,
                Type = type,
                Status = (byte)0,
                Title = title,
                SenderName = Self.Name,
                Attachments = attachments,
                ReceiverName = receiverName,
                OpenDate = DateTime.MinValue,
                Returned = (byte)0,
                Extra = extra
            };
            if (senderName != "")
                mailTemplate.SenderName = senderName;

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
                        /*
                        MailManager.Instance.allMailItems.Add(tempItem.Id, (tempItem, Self.Id));
                        mailItemIds.Add(MailManager.Instance.allMailItems[tempItem.Id].Item1);
                        */
                        /*
                        InventoryHelper.RemoveItemAndUpdateClient(Self, tempItem, tempItem.Count,false);
                        if (Self.Inventory.GetItem(mailSlots.Item1, mailSlots.Item2) == null)
                        {
                            tempItem.Slot = -1;
                            tempItem.SlotType = SlotType.Mail;
                            MailManager.Instance.allMailItems.Add(tempItem.Id, (tempItem, Self.AccountId));
                            mailItems.Add(MailManager.Instance.allMailItems[tempItem.Id].Item1);
                        }
                        */
                    }
                }
                /*
                else
                {
                    mailItemIds.Add(null);
                }
                */
            }
            var mailBodyTemplate = new MailBody()
            {
                Id = mailTemplate.Id,
                Type = mailTemplate.Type,
                ReceiverName = mailTemplate.ReceiverName,
                Title = mailTemplate.Title,
                Text = text,
                MoneyAmount1 = moneyAmounts[0],
                MoneyAmount2 = moneyAmounts[1],
                MoneyAmount3 = moneyAmounts[2],
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
                OpenDate = mailTemplate.OpenDate,
                //Attachments = mailItemIds
            };
            foreach(var iId in mailItemIds)
            {
                var i = Self.Inventory.GetItem(iId);
                if (i != null)
                {
                    mailBodyTemplate.AttachmentItemIds.Add(i.Id);
                    mailBodyTemplate.Attachments.Add(i);
                }
            }

            MailManager.Instance.allPlayerMails.Add(MailManager.Instance.highestMailID, new Tuple<Mail, MailBody>(mailTemplate, mailBodyTemplate));

            Self.SendPacket(new SCMailSentPacket(mailTemplate, itemSlots.ToArray()));
            var mailFee = mailBodyTemplate.AttachmentItemIds.Count * 30;
            if (mailTemplate.Type == 1)
                mailFee += 10 ;
            else if (mailTemplate.Type == 2)
                mailFee += 100 ;
            Self.ChangeMoney(SlotType.Inventory, -mailFee);
            MailManager.Instance.NotifyNewMailByNameIfOnline(mailTemplate, mailBodyTemplate, receiverName);
        }

        public void GetAttached(long id, bool money, bool items, bool takeAllSelected)
        {
            if (MailManager.Instance.allPlayerMails.ContainsKey(id))
            {
                var thisMail = MailManager.Instance.allPlayerMails[id];
                if (thisMail.Item2.MoneyAmount1 > 0 && money)
                {
                    Self.ChangeMoney(SlotType.Inventory, thisMail.Item2.MoneyAmount1);
                    thisMail.Item2.MoneyAmount1 = 0;
                    thisMail.Item1.Attachments -= 1;
                }
                var itemIDList = new List<ulong>();
                var itemSlotList = new List<(SlotType, byte)>();
                if (items)
                {
                    foreach (var itemAttachment in thisMail.Item2.Attachments)
                    {
                        if (itemAttachment.Id != 0)
                        {
                            itemIDList.Add(itemAttachment.Id);
                            itemAttachment.SlotType = SlotType.Inventory;
                            itemAttachment.Slot = -1;
                            InventoryHelper.AddItemAndUpdateClient(Self, itemAttachment);
                            thisMail.Item1.Attachments -= 1;
                            MailManager.Instance.allMailItemsId.Remove(id);
                            itemSlotList.Add((itemAttachment.SlotType, (byte)itemAttachment.Slot));
                        }
                        else
                            itemSlotList.Add((SlotType.None, (byte)0));
                    }
                    thisMail.Item2.Attachments.Clear(); // = new Item[10];
                    thisMail.Item2.AttachmentItemIds.Clear(); // = new Item[10];
                    // TODO: Handle full inventory
                }
                Self.SendPacket(new SCAttachmentTakenPacket(id, money, false, takeAllSelected, itemIDList.ToArray(), itemSlotList.ToArray()));
                Self.SendPacket(new SCMailStatusUpdatedPacket(false, id, 1));
            }
        }

        public void DeleteMail(long id, bool isSent)
        {
            if (MailManager.Instance.allPlayerMails.ContainsKey(id) && !isSent)
            {
                if (MailManager.Instance.allPlayerMails[id].Item1.Attachments <= 0)
                {
                    if (MailManager.Instance.allPlayerMails[id].Item1.Status == 0)
                        Self.SendPacket(new SCMailDeletedPacket(isSent, id, true, unreadMailCount));
                    else
                        Self.SendPacket(new SCMailDeletedPacket(isSent, id, false, unreadMailCount));
                    MailManager.Instance.allPlayerMails.Remove(id);
                }
            }
        }

        public void ReturnMail(long id)
        {
            if (MailManager.Instance.allPlayerMails.ContainsKey(id))
            {
                var itemSlots = new List<(SlotType slotType, byte slot)>();
                for (var i = 0; i < 10; i++)
                {
                    var item = Self.Inventory.GetItem(MailManager.Instance.allPlayerMails[id].Item2.Attachments[i].Id);
                    if (item.SlotType == SlotType.None)
                        itemSlots.Add(((byte)0, (byte)0));
                    else
                        itemSlots.Add((item.SlotType, (byte)item.Slot));
                }

                SendMail(MailManager.Instance.allPlayerMails[id].Item1.Type, MailManager.Instance.allPlayerMails[id].Item1.SenderName,
                        MailManager.Instance.allPlayerMails[id].Item1.ReceiverName, MailManager.Instance.allPlayerMails[id].Item1.Title, 
                        MailManager.Instance.allPlayerMails[id].Item2.Text, MailManager.Instance.allPlayerMails[id].Item1.Attachments, 
                        new int[] { MailManager.Instance.allPlayerMails[id].Item2.MoneyAmount1,
                                    MailManager.Instance.allPlayerMails[id].Item2.MoneyAmount2,
                                    MailManager.Instance.allPlayerMails[id].Item2.MoneyAmount3},
                        MailManager.Instance.allPlayerMails[id].Item1.Extra, itemSlots);
                DeleteMail(id, false);
            }
        }
    }
}
