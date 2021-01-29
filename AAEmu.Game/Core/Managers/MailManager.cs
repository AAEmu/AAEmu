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
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Features;

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
                            tempMail.OpenDate = reader.GetDateTime("open_date");

                            tempMail.Header.Status = (MailStatus)reader.GetInt32("status");
                            tempMail.Header.SenderId = reader.GetUInt32("sender_id");
                            tempMail.Header.SenderName = reader.GetString("sender_name");
                            tempMail.Header.Attachments = (byte)reader.GetInt32("attachment_count");
                            tempMail.Header.ReceiverId = reader.GetUInt32("receiver_id");
                            tempMail.Header.Returned = (reader.GetInt32("returned") != 0);
                            tempMail.Header.Extra = reader.GetInt64("extra");

                            tempMail.Body.Text = reader.GetString("text");
                            tempMail.Body.CopperCoins = reader.GetInt32("money_amount_1");
                            tempMail.Body.BillingAmount = reader.GetInt32("money_amount_2");
                            tempMail.Body.MoneyAmount2 = reader.GetInt32("money_amount_3");
                            tempMail.Body.SendDate = reader.GetDateTime("send_date");
                            tempMail.Body.RecvDate = reader.GetDateTime("received_date");

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
                            if (tempMail.Body.BillingAmount > 0)
                                attachmentCount++;
                            if (tempMail.Body.MoneyAmount2 > 0)
                                attachmentCount++;
                            if (attachmentCount != tempMail.Header.Attachments)
                                _log.Warn("Attachment count listed in mailId {0} did not match the number of attachments, possible mail or item corruption !", tempMail.Id);
                            // Reset the attachment counter
                            tempMail.Header.Attachments = (byte)attachmentCount;

                            // Set internal delivered flag
                            tempMail.IsDelivered = (tempMail.Body.RecvDate <= DateTime.UtcNow);
                            tempMail.IsDirty = false;

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
                if (!mtbs.Value.IsDirty)
                    continue;
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
                    command.Parameters.AddWithValue("@money2", mtbs.Value.Body.BillingAmount);
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
                    mtbs.Value.IsDirty = false;
                }

            }

            return (updatedCount, deletedCount);
        }

        #endregion

        public Dictionary<long, BaseMail> GetCurrentMailList(Character character)
        {
            var tempMails = _allPlayerMails.Where(x => x.Value.Body.RecvDate <= DateTime.UtcNow && (x.Value.Header.ReceiverId == character.Id || x.Value.Header.SenderId == character.Id)).ToDictionary(x => x.Key, x => x.Value);
            character.Mails.unreadMailCount.Received = 0;
            foreach (var mail in tempMails)
            {
                //if ((mail.Value.Header.Status != MailStatus.Read) && (mail.Value.Header.SenderId != character.Id))
                if (mail.Value.Header.Status != MailStatus.Read)
                {
                    character.Mails.unreadMailCount.Received += 1;
                    character.SendPacket(new SCGotMailPacket(mail.Value.Header, character.Mails.unreadMailCount, false, null));
                    mail.Value.IsDelivered = true;
                }
            }
            return tempMails;
        }

        public bool NotifyNewMailByNameIfOnline(BaseMail m, string receiverName)
        {
            _log.Trace("NotifyNewMailByNameIfOnline() - {0}", receiverName);
            // If unread and ready to deliver
            if ((m.Header.Status != MailStatus.Read) && (m.Body.RecvDate <= DateTime.UtcNow) && (m.IsDelivered == false))
            {
                var player = WorldManager.Instance.GetCharacter(receiverName);
                if (player != null)
                {
                    player.Mails.unreadMailCount.Received++;
                    player.SendPacket(new SCGotMailPacket(m.Header, player.Mails.unreadMailCount, false, null));
                    m.IsDelivered = true;
                    return true;
                }
            }
            return false;
        }

        public bool NotifyDeleteMailByNameIfOnline(BaseMail m, string receiverName)
        {
            _log.Trace("NotifyDeleteMailByNameIfOnline() - {0}", receiverName);
            var player = WorldManager.Instance.GetCharacter(receiverName);
            if (player != null)
            {
                if (m.Header.Status != MailStatus.Read)
                    player.Mails.unreadMailCount.Received--;
                player.SendPacket(new SCMailDeletedPacket(false, m.Id, true, player.Mails.unreadMailCount));
                return true;
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

        public bool PayChargeMoney(Character character, long mailId, bool autoUseAAPoint)
        {
            var mail = GetMailById(mailId);
            if (mail == null)
            {
                character.SendErrorMessage(ErrorMessageType.MailInvalid);
                return false;
            }

            // Only tax mail supported
            if (mail.MailType != MailType.Billing)
            {
                character.SendErrorMessage(ErrorMessageType.MailInvalid);
                return false;
            }

            var houseId = (uint)(mail.Header.Extra & 0xFFFFFFFF); // Extract house DB Id from Extra
            var houseZoneGroup = ((mail.Header.Extra >> 48) & 0xFFFF); // Extract zone group Id from Extra
            var house = HousingManager.Instance.GetHouseById(houseId);

            if (house == null)
            {
                character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return false;
            }

            if (FeaturesManager.Fsets.Check(Feature.taxItem))
            {
                // use Tax Certificates as payment
                // TODO: grab these values from DB somewhere ?
                var userTaxCount = character.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
                var userBoundTaxCount = character.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
                var totatUserTaxCount = userTaxCount + userBoundTaxCount;
                var consumedCerts = (int)Math.Ceiling(mail.Body.BillingAmount / 10000f);

                if (totatUserTaxCount < consumedCerts)
                {
                    // Not enough certs
                    character.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                    return false;
                }
                else
                {
                    var c = consumedCerts;
                    // Use Bound First
                    if ((userBoundTaxCount > 0) && (c > 0))
                    {
                        if (c > userBoundTaxCount)
                            c = userBoundTaxCount;
                        character.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.Mail, Item.BoundTaxCertificate, c, null);
                        consumedCerts -= c;
                    }
                    c = consumedCerts;
                    if ((userTaxCount > 0) && (c > 0))
                    {
                        if (c > userTaxCount)
                            c = userTaxCount;
                        character.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.Mail, Item.TaxCertificate, c, null);
                        consumedCerts -= c;
                    }

                    if (consumedCerts != 0)
                        _log.Error("Something went wrong when paying tax for mailId {0}", mail.Id);

                    mail.Body.BillingAmount = consumedCerts ;

                }
            }
            else
            {
                // use gold as payment
                if (mail.Body.BillingAmount > character.Money)
                {
                    // Not enough gold
                    character.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                    return false;
                }
                else
                {
                    character.SubtractMoney(SlotType.Inventory, mail.Body.BillingAmount);
                }

            }

            if (!HousingManager.Instance.PayWeeklyTax(house))
                _log.Error("Could not update protection time when paying taxes, mailId {0}", mail.Id);
            else
            {
                if (mail.Header.Status != MailStatus.Read)
                {
                    mail.Header.Status = MailStatus.Read;
                    character.Mails.unreadMailCount.Received--;
                }

                character.SendPacket(new SCChargeMoneyPaid(mail.Id));
                character.SendPacket(new SCMailDeletedPacket(false, mail.Id, false, character.Mails.unreadMailCount));
                DeleteMail(mail);
                character.Mails.SendUnreadMailCount();
            }

            return true;
        }

        public void ExtractExtraForHouse(long extra, out ushort zoneGroupId, out uint houseId)
        {
            houseId = (uint)(extra & 0xFFFFFFFF); // Extract house DB Id from Extra
            zoneGroupId = (ushort)((extra >> 48) & 0xFFFF); // Extract zone group Id from Extra
        }


        public void DeleteHouseMails(uint houseId)
        {
            var deleteList = new List<long>();
            // Check which mails to remove
            foreach(var m in _allPlayerMails)
            {
                if (m.Value.MailType == MailType.Billing)
                {
                    ExtractExtraForHouse(m.Value.Header.Extra, out _, out var hId);
                    if (houseId == hId)
                    {
                        deleteList.Add(m.Value.Id);
                    }
                }
            }
            // Actually remove them by Id
            foreach (var d in deleteList)
            {
                var mail = GetMailById(d);
                NotifyDeleteMailByNameIfOnline(mail, mail.ReceiverName);
                DeleteMail(mail);
            }
        }
        
        public List<BaseMail> GetMyHouseMails(uint houseId)
        {
            var resultList = new List<BaseMail>();
            // Check which mails to remove
            foreach(var m in _allPlayerMails)
            {
                if (m.Value.MailType == MailType.Billing)
                {
                    ExtractExtraForHouse(m.Value.Header.Extra, out _, out var hId);
                    if (houseId == hId)
                    {
                        resultList.Add(m.Value);
                    }
                }
            }
            return resultList;
        }
        
    }
}
