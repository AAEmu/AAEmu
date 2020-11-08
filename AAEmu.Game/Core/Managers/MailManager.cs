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
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Tasks.Mails;

namespace AAEmu.Game.Core.Managers
{

    public class MailManager : Singleton<MailManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Dictionary<long, BaseMail> _allPlayerMails;
        private List<long> _deletedMailIds = new List<long>();
        private object _lock = new object();

        public static int CostNormal = 50;
        public static int CostNormalAttachment = 30;
        public static int CostExpress = 100;
        public static int CostExpressAttachment = 80;
        public static int CostFreeAttachmentCount = 1;
        public static TimeSpan NormalMailDelay = TimeSpan.FromMinutes(30); // Default is 30 minutes
        public static TimeSpan MailExpireDelay = TimeSpan.FromDays(14);    // Default is 30 days ?

        public BaseMail GetMailById(long id)
        {
            if (_allPlayerMails.TryGetValue(id, out var theMail))
                return theMail;
            else
                return null;
        }

        public uint GetNewMailId()
        {
            lock (_deletedMailIds)
            {
                var Id = MailIdManager.Instance.GetNextId();
                if (_deletedMailIds.Contains(Id))
                    _deletedMailIds.Remove(Id);
                return Id;
            }
        }

        public bool Send(BaseMail mail)
        {
            // Verify Receiver
            var targetName = NameManager.Instance.GetCharacterName(mail.Header.ReceiverId);
            var targetId = NameManager.Instance.GetCharacterId(mail.Header.ReceiverName);
            if (!string.Equals(targetName, mail.Header.ReceiverName, StringComparison.InvariantCultureIgnoreCase))
            {
                _log.Debug("Send() - Failed to verify receiver name {0} != {1}", targetName, mail.Header.ReceiverName);
                return false; // Name mismatch
            }
            if (targetId != mail.Header.ReceiverId)
            {
                _log.Debug("Send() - Failed to verify receiver id {0} != {1}", targetId, mail.Header.ReceiverId);
                return false; // Id mismatch
            }

            // Assign a Id if we didn't have one yet
            if (mail.Id <= 0)
            {
                _log.Trace("Send() - Assign new mail Id");
                mail.Id = GetNewMailId();
            }
            _allPlayerMails.Add(mail.Id, mail);
            NotifyNewMailByNameIfOnline(mail, targetName);
            return true;
        }

        [Obsolete("SendMail() is deprecated. Use Send() of a BaseMail descendant instead.")]
        public void SendMail(MailType type, string receiverName, string senderName, string title, string text, byte attachments, int[] moneyAmounts, long extra, List<Item> items)
        {
            var mailTemplate = new BaseMail();
            mailTemplate.Id = MailIdManager.Instance.GetNextId();
            var senderId = NameManager.Instance.GetCharacterId(senderName);
            var receiverId = NameManager.Instance.GetCharacterId(receiverName);

            mailTemplate.Header = new MailHeader()
            {
                mailId = mailTemplate.Id,
                Type = type,
                Status = MailStatus.Unread,
                Title = title,
                SenderId = senderId,
                SenderName = senderName,
                Attachments = attachments,
                ReceiverId = receiverId,
                ReceiverName = receiverName,
                OpenDate = DateTime.MinValue,
                Returned = false,
                Extra = 0
            };

            foreach (var item in items)
            {
                if (item != null)
                {
                    item.SlotType = SlotType.Mail;
                    item.OwnerId = receiverId;
                }
            }

            mailTemplate.Body = new MailBody()
            {
                mailId = mailTemplate.Id,
                Type = mailTemplate.Header.Type,
                ReceiverName = mailTemplate.Header.ReceiverName,
                Title = mailTemplate.Header.Title,
                Text = text,
                CopperCoins = moneyAmounts[0],
                MoneyAmount1 = moneyAmounts[1],
                MoneyAmount2 = moneyAmounts[2],
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
                OpenDate = mailTemplate.Header.OpenDate,
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

            // Remove from delete list if it's a recycled Id
            if (_deletedMailIds.Contains(mailTemplate.Id))
                _deletedMailIds.Remove(mailTemplate.Id);
            _allPlayerMails.Add(mailTemplate.Id, mailTemplate);
            NotifyNewMailByNameIfOnline(mailTemplate, receiverName);
        }

        public bool DeleteMail(long id)
        {
            lock (_deletedMailIds)
            {
                if (!_deletedMailIds.Contains(id))
                    _deletedMailIds.Add(id);
                MailIdManager.Instance.ReleaseId((uint)id);
            }
            return _allPlayerMails.Remove(id);
        }

        public bool DeleteMail(BaseMail mail)
        {
            return DeleteMail(mail.Id);
        }

        #region Database
        public void Load()
        {
            _log.Info("Loading player mails ...");
            _allPlayerMails = new Dictionary<long, BaseMail>();
            _deletedMailIds = new List<long>();

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
                            var tempMail = new BaseMail();
                            tempMail.Id = reader.GetInt32("id");
                            tempMail.Title = reader.GetString("title");
                            tempMail.MailType = (MailType)reader.GetInt32("type");
                            tempMail.ReceiverName = reader.GetString("receiver_name");

                            tempMail.Header.Status = (MailStatus)reader.GetInt32("status");
                            tempMail.Header.SenderId = reader.GetUInt32("sender_id");
                            tempMail.Header.SenderName = reader.GetString("sender_name");
                            tempMail.Header.Attachments = (byte)reader.GetInt32("attachment_count");
                            tempMail.Header.ReceiverId = reader.GetUInt32("receiver_id");
                            tempMail.Header.OpenDate = reader.GetDateTime("open_date");
                            tempMail.Header.Returned = (reader.GetInt32("returned") != 0);
                            tempMail.Header.Extra = reader.GetUInt32("extra");

                            tempMail.Body.Text = reader.GetString("text");
                            tempMail.Body.CopperCoins = reader.GetInt32("money_amount_1");
                            tempMail.Body.MoneyAmount1 = reader.GetInt32("money_amount_2");
                            tempMail.Body.MoneyAmount2 = reader.GetInt32("money_amount_3");
                            tempMail.Body.SendDate = reader.GetDateTime("send_date");
                            tempMail.Body.RecvDate = reader.GetDateTime("received_date");
                            tempMail.Body.OpenDate = tempMail.Header.OpenDate;

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
                            if (tempMail.Body.CopperCoins > 0)
                                attachmentCount++;
                            if (tempMail.Body.MoneyAmount1 > 0)
                                attachmentCount++;
                            if (tempMail.Body.MoneyAmount2 > 0)
                                attachmentCount++;
                            if (attachmentCount != tempMail.Header.Attachments)
                                _log.Warn("Attachment count listed in mailId {0} did not match the number of attachments, possible mail or item corruption !");
                            // Reset the attachment counter
                            tempMail.Header.Attachments = (byte)attachmentCount;

                            // Set internal delivered flag
                            tempMail.IsDelivered = (tempMail.Body.RecvDate <= DateTime.UtcNow);

                            // Remove from delete list if it's a recycled Id
                            if (_deletedMailIds.Contains(tempMail.Id))
                                _deletedMailIds.Remove(tempMail.Id);
                            _allPlayerMails.Add(tempMail.Id, tempMail);
                        }
                    }
                }
                
                
            }
            _log.Info("Loaded {0} player mails", _allPlayerMails.Count);

            var mailCheckTask = new MailDeliveryTask();
            TaskManager.Instance.Schedule(mailCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
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
                    command.Parameters.AddWithValue("@type", (byte)mtbs.Value.Header.Type);
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
                    command.Parameters.AddWithValue("@returned", mtbs.Value.Header.Returned ? 1 : 0);
                    command.Parameters.AddWithValue("@extra", mtbs.Value.Header.Extra);
                    command.Parameters.AddWithValue("@money1", mtbs.Value.Body.CopperCoins);
                    command.Parameters.AddWithValue("@money2", mtbs.Value.Body.MoneyAmount1);
                    command.Parameters.AddWithValue("@money3", mtbs.Value.Body.MoneyAmount2);

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

        public Dictionary<long, BaseMail> GetCurrentMailList(Character c)
        {
            var tempMails = _allPlayerMails.Where(x => x.Value.Body.RecvDate <= DateTime.UtcNow && (x.Value.Header.ReceiverId == c.Id || x.Value.Header.SenderId == c.Id)).ToDictionary(x => x.Key, x => x.Value);
            c.Mails.unreadMailCount.Received = 0;
            foreach (var mail in tempMails)
            {
                if ((mail.Value.Header.Status == 0) && (mail.Value.Header.SenderId != c.Id))
                {
                    c.Mails.unreadMailCount.Received += 1;
                    c.SendPacket(new SCGotMailPacket(mail.Value.Header, c.Mails.unreadMailCount, false, null));
                    mail.Value.IsDelivered = true;
                }
            }
            return tempMails;
        }

        public bool NotifyNewMailByNameIfOnline(BaseMail m, string receiverName)
        {
            _log.Trace("NotifyNewMailByNameIfOnline() - {0}", receiverName);
            // If unread and ready to deliver
            if ((m.Header.Status == 0) && (m.Body.RecvDate <= DateTime.UtcNow))
            {
                var player = WorldManager.Instance.GetCharacter(receiverName);
                if (player != null)
                {
                    player.Mails.unreadMailCount.Received += 1;
                    player.SendPacket(new SCGotMailPacket(m.Header, player.Mails.unreadMailCount, false, null));
                    m.IsDelivered = true;
                    return true;
                }
            }
            return false;
        }

        public void CheckAllMailTimings()
        {
            // Deliver yet "undelivered" mails
            _log.Trace("CheckAllMailTimings");
            var undeliveredMails = _allPlayerMails.Where(x => (x.Value.Body.RecvDate <= DateTime.UtcNow) && (x.Value.IsDelivered == false)).ToDictionary(x => x.Key, x => x.Value);
            var delivered = 0;
            foreach (var mail in undeliveredMails)
                if (NotifyNewMailByNameIfOnline(mail.Value, mail.Value.Header.ReceiverName))
                    delivered++;
            if (delivered > 0)
                _log.Debug("{0}/{1} mail(s) delivered", delivered, undeliveredMails.Count);

            // TODO: Return expired mails back to owner if undelivered/unread
        }

    }
}
