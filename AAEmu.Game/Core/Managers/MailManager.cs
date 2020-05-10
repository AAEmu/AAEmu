﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using NLog;
using AAEmu.Game.Models.Game.Mails;
using MySql.Data.MySqlClient;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Commons.Network;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers
{
    public class MailManager : Singleton<MailManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Dictionary<long, Tuple<Mail, MailBody>> allPlayerMails;
        public Dictionary<long, ulong[]> allMailItemsId;

        public long highestMailID;

        public void SendMail(byte type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<Item> items)
        {
            var mailTemplate = new Mail()
            {
                Id = highestMailID += 1,
                Type = type,
                Status = (byte)0,
                Title = title,
                SenderName = senderName,
                Attachments = attachments,
                ReceiverName = receiverName,
                OpenDate = DateTime.MinValue,
                Returned = (byte)0,
                Extra = 0
            };

            foreach (var item in items)
            {
                if (item != null)
                {
                    item.SlotType = SlotType.Mail;
                    //allMailItems.Add(item.Id, (item, 0));
                }
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
                //ItemIds = items.ToArray()
            };
            var newOwnerId = NameManager.Instance.GetCharacterId(receiverName);
            foreach (var i in items)
            {
                if (i != null)
                {
                    mailBodyTemplate.Attachments.Add(i);
                }
            }

            allPlayerMails.Add(highestMailID, new Tuple<Mail, MailBody>(mailTemplate, mailBodyTemplate));
            NotifyNewMailByNameIfOnline(mailTemplate, mailBodyTemplate, receiverName);
        }

        #region Database
        public void Load()
        {
            _log.Info("Loading player mails...");
            allPlayerMails = new Dictionary<long, Tuple<Mail, MailBody>>();
            allMailItemsId = new Dictionary<long, ulong[]>();
            //allMailItems = new Dictionary<ulong, (Item, uint)>();
            highestMailID = 0;
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM mails";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var tempMail = new Mail()
                            {
                                Id = reader.GetInt32("id"),
                                Type = (byte)reader.GetInt32("type"),
                                Status = (byte)reader.GetInt32("status"),
                                Title = reader.GetString("title"),
                                SenderName = reader.GetString("sender_name"),
                                Attachments = (byte)reader.GetInt32("attachments"),
                                ReceiverName = reader.GetString("receiver_name"),
                                OpenDate = reader.GetDateTime("open_date"),
                                Returned = (byte)reader.GetInt32("returned"),
                                Extra = reader.GetUInt32("extra")
                            };
                            var tempMailBody = new MailBody()
                            {
                                Id = tempMail.Id,
                                Type = tempMail.Type,
                                ReceiverName = tempMail.ReceiverName,
                                Title = tempMail.Title,
                                Text = reader.GetString("text"),
                                MoneyAmount1 = reader.GetInt32("money_amount_1"),
                                MoneyAmount2 = reader.GetInt32("money_amount_2"),
                                MoneyAmount3 = reader.GetInt32("money_amount_3"),
                                SendDate = reader.GetDateTime("send_date"),
                                RecvDate = reader.GetDateTime("received_date"),
                                OpenDate = tempMail.OpenDate,
                                // ItemIds = new Item[10] //TODO: Pull items from DB instead
                            };

                            if (highestMailID < tempMail.Id)
                                highestMailID = tempMail.Id;
                            allPlayerMails.Add(tempMail.Id, new Tuple<Mail, MailBody>(tempMail, tempMailBody));
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM mails_items";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetInt64("id");

                            ulong[] itemIDs = new ulong[10];
                            for (int i = 0; i < 10; i++)
                            {
                                try
                                {
                                    itemIDs[i] = reader.GetUInt64(string.Format("item{0}", i));
                                }
                                catch (System.Data.SqlTypes.SqlNullValueException e)
                                {
                                    _log.Error(e);
                                }
                            }

                            if (allPlayerMails.TryGetValue(id, out var targetMail))
                            {
                                foreach(var iId in itemIDs)
                                    if (iId != 0)
                                targetMail.Item2.AttachmentItemIds.Add(iId);
                            }

                            allMailItemsId.Add(id, itemIDs);
                        }
                    }
                }

                /*
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM items WHERE `slot_type` = @slotType";
                    command.Prepare();
                    command.Parameters.AddWithValue("@slotType", "Mail");
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var type = reader.GetString("type");
                            Type nClass = null;
                            try
                            {
                                nClass = Type.GetType(type);
                            }
                            catch (Exception ex)
                            {
                                _log.Error(ex);
                            }

                            if (nClass == null)
                            {
                                _log.Error("Item type {0} not found!", type);
                                continue;
                            }

                            Item item;
                            try
                            {
                                item = (Item)Activator.CreateInstance(nClass);
                            }
                            catch (Exception ex)
                            {
                                _log.Error(ex);
                                _log.Error(ex.InnerException);
                                item = new Item();
                            }

                            item.Id = reader.GetUInt64("id");
                            item.TemplateId = reader.GetUInt32("template_id");
                            item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
                            item.SlotType = (SlotType)Enum.Parse(typeof(SlotType), reader.GetString("slot_type"), true);
                            item.Slot = reader.GetInt32("slot");
                            item.Count = reader.GetInt32("count");
                            item.LifespanMins = reader.GetInt32("lifespan_mins");
                            item.MadeUnitId = reader.GetUInt32("made_unit_id");
                            item.UnsecureTime = reader.GetDateTime("unsecure_time");
                            item.UnpackTime = reader.GetDateTime("unpack_time");
                            item.CreateTime = reader.GetDateTime("created_at");
                            var details = (PacketStream)(byte[])reader.GetValue("details");
                            item.ReadDetails(details);

                            if (item.Template.FixedGrade >= 0)
                                item.Grade = (byte)item.Template.FixedGrade; // Overwrite Fixed-grade items, just to make sure
                            else if (item.Template.Gradable)
                                item.Grade = reader.GetByte("grade"); // Load from our DB if the item is gradable

                            var owner = reader.GetUInt32("owner");

                            if (item.SlotType == SlotType.Mail)
                                allMailItems.Add(item.Id, (item, owner));
                        }
                    }
                }
                */
                
                
            }
            _log.Info("Loaded {0} player mails", allPlayerMails.Count);
            // _log.Info("Loaded {0} player mails & {1} player mail items", allPlayerMails.Count, allMailItems.Count);
        }

        public void Save()
        {
            using (var connection = MySQL.CreateConnection())
            {
                using (var transaction = connection.BeginTransaction())
                {
                    _log.Info("Deleting old DB mail data");
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;

                        command.CommandText = "TRUNCATE TABLE mails";
                        command.Prepare();
                        command.ExecuteNonQuery();
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;

                        command.CommandText = "TRUNCATE TABLE mails_items";
                        command.Prepare();
                        command.ExecuteNonQuery();
                    }
                    _log.Info("Done deleting old mail data & starting inserting new data from memory");
                    foreach (var mtbs in allPlayerMails)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.Connection = connection;
                            command.Transaction = transaction;
                            command.CommandText = "INSERT INTO mails(`id`,`type`,`status`,`title`,`text`,`sender_name`,`attachments`, " +
                                                  "`receiver_name`,`open_date`,`send_date`,`received_date`,`returned`,`extra`,`money_amount_1`," +
                                                  "`money_amount_2`,`money_amount_3`) VALUES (@id, @type, @status, @title, @text, @senderName," +
                                                  " @attachments, @receiverName, @openDate, @sendDate, @receivedDate, @returned, @extra, @money1, @money2, @money3)";
                            command.Prepare();
                            command.Parameters.AddWithValue("@id", mtbs.Value.Item1.Id);
                            command.Parameters.AddWithValue("@openDate", mtbs.Value.Item1.OpenDate);
                            command.Parameters.AddWithValue("@type", mtbs.Value.Item1.Type);
                            command.Parameters.AddWithValue("@status", mtbs.Value.Item1.Status);
                            command.Parameters.AddWithValue("@title", mtbs.Value.Item1.Title);
                            command.Parameters.AddWithValue("@text", mtbs.Value.Item2.Text);
                            command.Parameters.AddWithValue("@senderName", mtbs.Value.Item1.SenderName);
                            command.Parameters.AddWithValue("@attachments", mtbs.Value.Item1.Attachments);
                            command.Parameters.AddWithValue("@receiverName", mtbs.Value.Item1.ReceiverName);
                            command.Parameters.AddWithValue("@sendDate", mtbs.Value.Item2.SendDate);
                            command.Parameters.AddWithValue("@receivedDate", mtbs.Value.Item2.RecvDate);
                            command.Parameters.AddWithValue("@returned", mtbs.Value.Item1.Returned);
                            command.Parameters.AddWithValue("@extra", mtbs.Value.Item1.Extra);
                            command.Parameters.AddWithValue("@money1", mtbs.Value.Item2.MoneyAmount1);
                            command.Parameters.AddWithValue("@money2", mtbs.Value.Item2.MoneyAmount2);
                            command.Parameters.AddWithValue("@money3", mtbs.Value.Item2.MoneyAmount3);
                            command.ExecuteNonQuery();
                        }

                        using (var command = connection.CreateCommand())
                        {
                            command.Connection = connection;
                            command.Transaction = transaction;
                            command.CommandText = "INSERT INTO mails_items (`id`,`item0`,`item1`,`item2`,`item3`,`item4`,`item5`, " +
                                                  "`item6`,`item7`,`item8`,`item9`) VALUES (@id, @item0, @item1, @item2, @item3," +
                                                  " @item4, @item5, @item6, @item7, @item8, @item9)";
                            command.Prepare();
                            command.Parameters.AddWithValue("@id", mtbs.Value.Item1.Id);
                            for(var i = 0; i < 10;i++)
                            {
                                if (i >= mtbs.Value.Item2.Attachments.Count)
                                    command.Parameters.AddWithValue("@item" + i.ToString(), 0);
                                else
                                    command.Parameters.AddWithValue("@item" + i.ToString(), mtbs.Value.Item2.AttachmentItemIds[i]);
                            }
                            command.ExecuteNonQuery();
                        }
                    }
                    _log.Info("Done saving mails");
                }
            }
        }

        #endregion

        public Dictionary<long, Tuple<Mail, MailBody>> GetCurrentMailList(Character c, bool login)
        {
            var tempMail = allPlayerMails.Where(x => x.Value.Item1.ReceiverName == c.Name || x.Value.Item1.SenderName == c.Name).ToDictionary(x => x.Key, x => x.Value);
            c.Mails.unreadMailCount.Received = 0;
            foreach (var mail in tempMail)
            {
                if (mail.Value.Item1.Status == 0 && mail.Value.Item1.SenderName != c.Name)
                {
                    c.Mails.unreadMailCount.Received += 1;
                    c.SendPacket(new SCGotMailPacket(mail.Value.Item1, c.Mails.unreadMailCount, false, null));
                }
            }


            if (!login) //Don't calculate items into the mail for the player on login since we only need to alert them of a new mail until they open the mailbox 
            {
                foreach (var mail in tempMail)
                {
                    // get items from ItemIds
                    mail.Value.Item2.Attachments.Clear();
                    foreach (var itemId in mail.Value.Item2.AttachmentItemIds)
                    {
                        mail.Value.Item2.Attachments.Add(c.Inventory.GetItem(itemId));
                    }
                    /*
                    // var tempMailItemsID = allMailItemsId.Where(x => tempMail.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value);
                    var mailItems = new List<Item>();
                    foreach (var item in c.Inventory.MailItems)
                    {
                        if (item.SlotType == SlotType.Mail)
                        {
                            mailItems.Add(item);
                            if (allMailItems.ContainsKey(item.Id))
                                allMailItems[item.Id] = (item, c.Id);
                        }
                        else
                            mailItems.Add(null);
                    }
                    mail.Value.Item2.Attachments = mailItems ;
                    */
                }
            }
            return tempMail;
        }

        public void NotifyNewMailByNameIfOnline(Mail m, MailBody mb, string receiverName)
        {
            if (m.Status == 0)
            {
                var player = WorldManager.Instance.GetCharacter(receiverName);
                if (player != null)
                {
                    player.Mails.unreadMailCount.Received += 1;
                    player.SendPacket(new SCGotMailPacket(m, player.Mails.unreadMailCount, false, null));
                }
            }
        }
    }
}
