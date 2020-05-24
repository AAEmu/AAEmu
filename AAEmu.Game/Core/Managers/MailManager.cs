using System;
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
using NLog.Targets;
using System.ComponentModel.DataAnnotations;

namespace AAEmu.Game.Core.Managers
{
    public class Mail
    {
        public long Id { get; set; }
        public MailHeader Header { get; set; }
        public MailBody Body { get; set; }
    }

    public class MailManager : Singleton<MailManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Dictionary<long, Mail> _allPlayerMails;
        private List<long> _deletedMailIds;

        public long highestMailID;

        public void SendMail(byte type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<Item> items)
        {
            var mailTemplate = new Mail();
            // TODO: get this from a ID manager ?
            mailTemplate.Id = highestMailID += 1;
            var senderId = NameManager.Instance.GetCharacterId(senderName);
            var receiverId = NameManager.Instance.GetCharacterId(receiverName);

            mailTemplate.Header = new MailHeader()
            {
                mailId = mailTemplate.Id,
                Type = type,
                Status = (byte)0,
                Title = title,
                SenderId = senderId,
                SenderName = senderName,
                Attachments = attachments,
                ReceiverId = receiverId,
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

            mailTemplate.Body = new MailBody()
            {
                mailId = mailTemplate.Id,
                Type = mailTemplate.Header.Type,
                ReceiverName = mailTemplate.Header.ReceiverName,
                Title = mailTemplate.Header.Title,
                Text = text,
                MoneyAmount1 = moneyAmounts[0],
                MoneyAmount2 = moneyAmounts[1],
                MoneyAmount3 = moneyAmounts[2],
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
                OpenDate = mailTemplate.Header.OpenDate,
                //ItemIds = items.ToArray()
            };
            var newOwnerId = NameManager.Instance.GetCharacterId(receiverName);
            foreach (var i in items)
            {
                if (i != null)
                {
                    i.OwnerId = newOwnerId;
                    mailTemplate.Body.Attachments.Add(i);
                }
            }

            _allPlayerMails.Add(highestMailID, mailTemplate);
            NotifyNewMailByNameIfOnline(mailTemplate, receiverName);
        }

        public bool DeleteMail(long id)
        {
            lock (_deletedMailIds)
            {
                if (!_deletedMailIds.Contains(id))
                    _deletedMailIds.Add(id);
            }
            return _allPlayerMails.Remove(id);
        }

        public bool DeleteMail(Mail mail)
        {
            return DeleteMail(mail.Id);
        }

        #region Database
        public void Load()
        {
            _log.Info("Loading player mails ...");
            _allPlayerMails = new Dictionary<long, Mail>();
            _deletedMailIds = new List<long>();
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
                            var tempMail = new Mail();
                            tempMail.Id = reader.GetInt32("id");

                            tempMail.Header = new MailHeader()
                            {
                                mailId = tempMail.Id,
                                Type = (byte)reader.GetInt32("type"),
                                Status = (byte)reader.GetInt32("status"),
                                Title = reader.GetString("title"),
                                SenderId = reader.GetUInt32("sender_id"),
                                SenderName = reader.GetString("sender_name"),
                                Attachments = (byte)reader.GetInt32("attachment_count"),
                                ReceiverId = reader.GetUInt32("receiver_id"),
                                ReceiverName = reader.GetString("receiver_name"),
                                OpenDate = reader.GetDateTime("open_date"),
                                Returned = (byte)reader.GetInt32("returned"),
                                Extra = reader.GetUInt32("extra")
                            };
                            tempMail.Body = new MailBody()
                            {
                                mailId = tempMail.Id,
                                Type = tempMail.Header.Type,
                                ReceiverName = tempMail.Header.ReceiverName,
                                Title = tempMail.Header.Title,
                                Text = reader.GetString("text"),
                                MoneyAmount1 = reader.GetInt32("money_amount_1"),
                                MoneyAmount2 = reader.GetInt32("money_amount_2"),
                                MoneyAmount3 = reader.GetInt32("money_amount_3"),
                                SendDate = reader.GetDateTime("send_date"),
                                RecvDate = reader.GetDateTime("received_date"),
                                OpenDate = tempMail.Header.OpenDate,
                            };

                            // Read/Load Items
                            tempMail.Body.Attachments.Clear();
                            for (var i = 0; i < MailBody.MaxMailAttachments; i++)
                            {
                                var itemId = reader.GetUInt64("attachment" + i.ToString());
                                if (itemId > 0)
                                {
                                    var item = ItemManager.Instance.GetItemByItemId(itemId);
                                    if (item != null)
                                    {
                                        item.OwnerId = tempMail.Header.ReceiverId;
                                        tempMail.Body.Attachments.Add(item);
                                    }
                                    else
                                    {
                                        _log.Warn("Found orphaned itemId {0} in mailId {1}, not loaded!", itemId, tempMail.Id);
                                    }
                                }
                            }
                            var attachmentCount = tempMail.Body.Attachments.Count;
                            if (tempMail.Body.MoneyAmount1 > 0)
                                attachmentCount++;
                            if (tempMail.Body.MoneyAmount2 > 0)
                                attachmentCount++;
                            if (tempMail.Body.MoneyAmount3 > 0)
                                attachmentCount++;
                            if (attachmentCount != tempMail.Header.Attachments)
                                _log.Warn("Attachment count listed in mailId {0} did not match the number of attachments, possible mail or item corruption !");
                            // Reset the attachment counter
                            tempMail.Header.Attachments = (byte)attachmentCount;

                            if (highestMailID < tempMail.Id)
                                highestMailID = tempMail.Id;
                            _allPlayerMails.Add(tempMail.Id, tempMail);
                        }
                    }
                }

                
                
            }
            _log.Info("Loaded {0} player mails", _allPlayerMails.Count);
        }

        public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            var deletedCount = 0;
            var updatedCount = 0;
            // _log.Info("Saving mail data ...");

            lock (_deletedMailIds)
            {
                deletedCount = _deletedMailIds.Count;
                if (_deletedMailIds.Count > 0)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.Transaction = transaction;
                        command.CommandText = "DELETE FROM mails WHERE `id` IN(" + string.Join(",", _deletedMailIds) + ")";
                        command.Prepare();
                        command.ExecuteNonQuery();
                    }
                    _deletedMailIds.Clear();
                }
            }

            foreach (var mtbs in _allPlayerMails)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;
                    command.CommandText = "REPLACE INTO mails(" +
                        "`id`,`type`,`status`,`title`,`text`,`sender_id`,`sender_name`," +
                        "`attachment_count`,`receiver_id`,`receiver_name`,`open_date`,`send_date`,`received_date`," +
                        "`returned`,`extra`,`money_amount_1`,`money_amount_2`,`money_amount_3`," +
                        "`attachment0`,`attachment1`,`attachment2`,`attachment3`,`attachment4`,`attachment5`," +
                        "`attachment6`,`attachment7`,`attachment8`,`attachment9`" +
                        ") VALUES (" +
                        "@id, @type, @status, @title, @text, @senderId, @senderName, " +
                        "@attachment_count, @receiverId, @receiverName, @openDate, @sendDate, @receivedDate, " +
                        "@returned, @extra, @money1, @money2, @money3," +
                        "@attachment0, @attachment1, @attachment2, @attachment3, @attachment4, @attachment5, " +
                        "@attachment6, @attachment7, @attachment8, @attachment9" +
                        ")";

                    command.Prepare();
                    command.Parameters.AddWithValue("@id", mtbs.Value.Id);
                    command.Parameters.AddWithValue("@openDate", mtbs.Value.Header.OpenDate);
                    command.Parameters.AddWithValue("@type", mtbs.Value.Header.Type);
                    command.Parameters.AddWithValue("@status", mtbs.Value.Header.Status);
                    command.Parameters.AddWithValue("@title", mtbs.Value.Header.Title);
                    command.Parameters.AddWithValue("@text", mtbs.Value.Body.Text);
                    command.Parameters.AddWithValue("@senderId", mtbs.Value.Header.SenderId);
                    command.Parameters.AddWithValue("@senderName", mtbs.Value.Header.SenderName);
                    command.Parameters.AddWithValue("@attachment_count", mtbs.Value.Header.Attachments);
                    command.Parameters.AddWithValue("@receiverId", mtbs.Value.Header.ReceiverId);
                    command.Parameters.AddWithValue("@receiverName", mtbs.Value.Header.ReceiverName);
                    command.Parameters.AddWithValue("@sendDate", mtbs.Value.Body.SendDate);
                    command.Parameters.AddWithValue("@receivedDate", mtbs.Value.Body.RecvDate);
                    command.Parameters.AddWithValue("@returned", mtbs.Value.Header.Returned);
                    command.Parameters.AddWithValue("@extra", mtbs.Value.Header.Extra);
                    command.Parameters.AddWithValue("@money1", mtbs.Value.Body.MoneyAmount1);
                    command.Parameters.AddWithValue("@money2", mtbs.Value.Body.MoneyAmount2);
                    command.Parameters.AddWithValue("@money3", mtbs.Value.Body.MoneyAmount3);

                    for (var i = 0; i < MailBody.MaxMailAttachments; i++)
                    {
                        if (i >= mtbs.Value.Body.Attachments.Count)
                            command.Parameters.AddWithValue("@attachment" + i.ToString(), 0);
                        else
                            command.Parameters.AddWithValue("@attachment" + i.ToString(), mtbs.Value.Body.Attachments[i].Id);
                    }

                    command.ExecuteNonQuery();
                    updatedCount++;
                }

            }

            return (updatedCount, deletedCount);
        }

        #endregion

        public Dictionary<long, Mail> GetCurrentMailList(Character c)
        {
            var tempMail = _allPlayerMails.Where(x => x.Value.Header.ReceiverId == c.Id || x.Value.Header.SenderId == c.Id).ToDictionary(x => x.Key, x => x.Value);
            c.Mails.unreadMailCount.Received = 0;
            foreach (var mail in tempMail)
            {
                if (mail.Value.Header.Status == 0 && mail.Value.Header.SenderId != c.Id)
                {
                    c.Mails.unreadMailCount.Received += 1;
                    c.SendPacket(new SCGotMailPacket(mail.Value.Header, c.Mails.unreadMailCount, false, null));
                }
            }
            return tempMail;
        }

        public void NotifyNewMailByNameIfOnline(Mail m, string receiverName)
        {
            if (m.Header.Status == 0)
            {
                var player = WorldManager.Instance.GetCharacter(receiverName);
                if (player != null)
                {
                    player.Mails.unreadMailCount.Received += 1;
                    player.SendPacket(new SCGotMailPacket(m.Header, player.Mails.unreadMailCount, false, null));
                }
            }
        }
    }
}
