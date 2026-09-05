using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using Microsoft.Extensions.DependencyInjection;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Tasks.Mails;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class MailManager(IMailIdManager mailIdManager, INameManager nameManager, IItemManager itemManager, ITaskManager taskManager, IWorldManager worldManager, Lazy<IHousingManager> housingManager, ILocalizationManager localizationManager) : Singleton<MailManager>, IMailManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Dictionary<long, BaseMail> _allPlayerMails = [];
    public Dictionary<long, BaseMail> AllPlayerMails => _allPlayerMails;
    private List<long> _deletedMailIds = [];
    // Unused: private object _lock = new();

    public static int CostNormal { get; set; } = 50;
    public static int CostNormalAttachment { get; set; } = 30;
    public static int CostExpress { get; set; } = 100;
    public static int CostExpressAttachment { get; set; } = 80;
    public static int CostFreeAttachmentCount { get; set; } = 1;
    public static TimeSpan NormalMailDelay { get; set; } = TimeSpan.FromMinutes(30); // Default is 30 minutes
    public static TimeSpan MailExpireDelay { get; set; } = TimeSpan.FromDays(14);    // Default is 30 days ?

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
            var Id = mailIdManager.GetNextId();
            if (_deletedMailIds.Contains(Id))
                _deletedMailIds.Remove(Id);
            return Id;
        }
    }

    public bool Send(BaseMail mail)
    {
        // Verify Receiver
        var targetName = nameManager.GetCharacterName(mail.Header.ReceiverId);
        var targetId = nameManager.GetCharacterId(mail.Header.ReceiverName);
        if (!string.Equals(targetName, mail.Header.ReceiverName, StringComparison.InvariantCultureIgnoreCase))
        {
            Logger.Debug("Send() - Failed to verify receiver name {0} != {1}", targetName, mail.Header.ReceiverName);
            return false; // Name mismatch
        }
        if (targetId != mail.Header.ReceiverId)
        {
            Logger.Debug("Send() - Failed to verify receiver id {0} != {1}", targetId, mail.Header.ReceiverId);
            return false; // Id mismatch
        }

        // Assign a Id if we didn't have one yet
        if (mail.Id <= 0)
        {
            Logger.Trace("Send() - Assign new mail Id");
            mail.Id = GetNewMailId();
        }
        _allPlayerMails ??= [];
        lock (_allPlayerMails)
        {
            if (_allPlayerMails.ContainsKey(mail.Id))
            {
                Logger.Error("Send() - Refusing to replace existing mail {0}", mail.Id);
                return false;
            }

            _allPlayerMails.Add(mail.Id, mail);
        }
        NotifyNewMailByNameIfOnline(mail, targetName);
        PersistNow();
        return true;
    }

    public bool TryReturnToSender(BaseMail mail)
    {
        return TryReturnToSenderCore(mail, null);
    }

    public bool TryReturnToSenderFor(BaseMail mail, uint characterId)
    {
        return TryReturnToSenderCore(mail, characterId);
    }

    /// <summary>
    /// Turns a tracked mail around without moving its attachments or removing/re-adding its dictionary entry.
    /// Eligibility, destination identity, and the tracked object reference are all validated under the same
    /// lock as the mutation so deletion and mail-id reuse cannot race the return.
    /// </summary>
    private bool TryReturnToSenderCore(BaseMail mail, uint? authorizedReceiverId)
    {
        if (mail == null)
            return false;

        uint originalReceiverId;
        string destinationName;
        lock (_allPlayerMails)
        {
            if (!_allPlayerMails.TryGetValue(mail.Id, out var trackedMail) ||
                !ReferenceEquals(trackedMail, mail))
            {
                Logger.Warn("TryReturnToSender - Mail {0} is no longer the tracked instance", mail.Id);
                return false;
            }

            var eligible = authorizedReceiverId.HasValue
                ? mail.CanBeReturnedBy(authorizedReceiverId.Value)
                : mail.CanReturnMail();
            if (!eligible)
                return false;

            var destinationId = mail.Header.SenderId;
            destinationName = mail.Header.SenderName;
            var registeredName = nameManager.GetCharacterName(destinationId);
            var registeredId = nameManager.GetCharacterId(destinationName);
            if (!string.Equals(registeredName, destinationName, StringComparison.InvariantCultureIgnoreCase) ||
                registeredId != destinationId)
            {
                Logger.Warn(
                    "TryReturnToSender - Destination identity mismatch for mail {0}: {1}({2}) resolved as {3}({4})",
                    mail.Id, destinationName, destinationId, registeredName, registeredId);
                return false;
            }

            originalReceiverId = mail.Header.ReceiverId;
            var originalReceiverName = mail.Header.ReceiverName;

            mail.Header.ReceiverId = destinationId;
            mail.ReceiverName = destinationName;
            mail.Header.SenderId = originalReceiverId;
            mail.Header.SenderName = originalReceiverName;
            mail.Header.Returned = true;
            mail.Header.Status = MailStatus.Unread;
            mail.IsDelivered = false;
        }

        var originalReceiver = worldManager.GetCharacterById(originalReceiverId);
        if (originalReceiver is { IsOnline: true })
            originalReceiver.SendPacket(new SCMailReturnedPacket(mail.Id, mail.Header));

        NotifyNewMailByNameIfOnline(mail, destinationName);
        PersistNow();
        return true;
    }

    [Obsolete("SendMail() is deprecated. Use Send() of a BaseMail descendant instead.")]
    public void SendMail(MailType type, string receiverName, string senderName, string title, string text,
        byte attachments, int[] moneyAmounts, long extra, List<Item> items)
    {
        throw new GameException("SendMail is deprecated, use BaseMail.Send() instead");
    }

    public bool DeleteMail(long id)
    {
        lock (_deletedMailIds)
        {
            if (!_deletedMailIds.Contains(id))
                _deletedMailIds.Add(id);
            mailIdManager.ReleaseId((uint)id);
        }

        bool removed;
        lock (_allPlayerMails)
            removed = _allPlayerMails.Remove(id);

        PersistNow();
        return removed;
    }

    public bool DeleteMail(BaseMail mail, bool trashItems = false)
    {
        if (trashItems)
        {
            for (var i = mail.Body.Attachments.Count - 1; i >= 0; i--)
            {
                try
                {
                    var item = mail.Body.Attachments[i];
                    if (item == null)
                        continue;

                    // WebAPI / GM Create never parents the item, so there is no container
                    // to remove from. Release the id instead of dereferencing null.
                    if (item._holdingContainer != null)
                        item._holdingContainer.RemoveItem(ItemTaskType.Invalid, item, true);
                    else
                        itemManager.ReleaseId(item.Id);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to remove mail attachment [{i}] from {mail.Id}: {ex}");
                }
            }
        }
        return DeleteMail(mail.Id);
    }

    #region Database
    public void Load()
    {
        Logger.Info("Loading player mails ...");
        _allPlayerMails = [];
        _deletedMailIds = [];

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
                        var tempMail = new BaseMail
                        {
                            Id = reader.GetInt32("id"), Title = reader.GetString("title"), MailType = (MailType)reader.GetInt32("type"),
                            ReceiverName = reader.GetString("receiver_name"),
                            OpenDate = reader.GetDateTime("open_date"),
                            Header =
                            {
                                Status = (MailStatus)reader.GetInt32("status"),
                                SenderId = reader.GetUInt32("sender_id"),
                                SenderName = reader.GetString("sender_name"),
                                Attachments = (byte)reader.GetInt32("attachment_count"),
                                ReceiverId = reader.GetUInt32("receiver_id"),
                                Returned = reader.GetInt32("returned") != 0,
                                Extra = reader.GetInt64("extra")
                            },
                            Body =
                            {
                                Text = reader.GetString("text"),
                                CopperCoins = reader.GetInt32("money_amount_1"),
                                BillingAmount = reader.GetInt32("money_amount_2"),
                                MoneyAmount2 = reader.GetInt32("money_amount_3"),
                                SendDate = reader.GetDateTime("send_date"),
                                RecvDate = reader.GetDateTime("received_date")
                            }
                        };

                        // Read/Load Items
                        tempMail.Body.Attachments.Clear();
                        for (var i = 0; i < MailBody.MaxMailAttachments; i++)
                        {
                            var itemId = reader.GetUInt64("attachment" + i.ToString());
                            if (itemId > 0)
                            {
                                var item = itemManager.GetItemByItemId(itemId);
                                if (MailAttachmentLoadRules.CanReload(item))
                                {
                                    item.OwnerId = tempMail.Header.ReceiverId;
                                    tempMail.Body.Attachments.Add(item);
                                }
                                else if (item != null)
                                {
                                    Logger.Warn(
                                        "Skipping mail {0} attachment item {1}: already claimed (slot={2})",
                                        tempMail.Id, itemId, item.SlotType);
                                }
                                else
                                {
                                    Logger.Warn("Found orphaned itemId {0} in mailId {1}, not loaded!", itemId, tempMail.Id);
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
                            Logger.Warn("Attachment count listed in mailId {0} did not match the number of attachments, possible mail or item corruption !", tempMail.Id);
                        // Reset the attachment counter
                        tempMail.Header.Attachments = (byte)attachmentCount;

                        // Set internal delivered flag
                        tempMail.IsDelivered = tempMail.Body.RecvDate <= DateTime.UtcNow;
                        tempMail.IsDirty = false;

                        // Remove from delete list if it's a recycled Id
                        if (_deletedMailIds.Contains(tempMail.Id))
                            _deletedMailIds.Remove(tempMail.Id);
                        _allPlayerMails.Add(tempMail.Id, tempMail);
                    }
                }
            }
        }
        Logger.Info("Loaded {0} player mails", _allPlayerMails.Count);

        var mailCheckTask = new MailDeliveryTask();
        taskManager.Schedule(mailCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deletedCount = 0;
        var updatedCount = 0;
        // Logger.Info("Saving mail data ...");

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

                command.Prepare();
                command.ExecuteNonQuery();
                updatedCount++;
                mtbs.Value.IsDirty = false;
            }
        }

        return (updatedCount, deletedCount);
    }

    /// <summary>
    /// Writes dirty mail (and the rest of the World snapshot) immediately.
    /// Claim/send/delete used to wait for the 5-minute tick; a killed World
    /// then reloaded the pre-claim row and the letter came back unclaimed.
    /// No-ops in tests that do not register <see cref="ISaveManager"/>.
    /// </summary>
    public void PersistNow()
    {
        var saver = SingletonContainer.ServiceProvider?.GetService<ISaveManager>();
        if (saver == null)
            return;

        if (!saver.DoSave())
            Logger.Warn("Mail persist skipped or failed (save already running?)");
    }

    #endregion

    
    public Dictionary<long, BaseMail> GetCurrentMailList(uint characterId)
    {
        // Try to grab the actual online Character object to send live updates
        var character = worldManager.GetCharacterById(characterId);
        var tempMails = _allPlayerMails.Where(
            x => x.Value.Body.RecvDate <= DateTime.UtcNow &&
                 (x.Value.Header.ReceiverId == characterId || 
                  x.Value.Header.SenderId == characterId)
                 ).
            ToDictionary(x => x.Key, x => x.Value);
        character?.Mails.UnreadMailCount.ResetReceived();
        character?.Mails.UnreadMailCount.ResetTotals();
        // Pre-pass: total counts every landed received mail (read + unread) so each pushed SCGotMail carries
        // the final total (the client uses the total, not unread, to size each mail-list tab).
        foreach (var mail in tempMails)
        {
            if (mail.Value.Header.ReceiverId == characterId)
                character?.Mails.UnreadMailCount.AddTotal(mail.Value.MailType);
        }
        foreach (var mail in tempMails)
        {
            //if ((mail.Value.Header.Status != MailStatus.Read) && (mail.Value.Header.SenderId != character.Id))
            if (mail.Value.Header.Status != MailStatus.Read)
            {
                character?.Mails.UnreadMailCount.UpdateReceived(mail.Value.MailType, 1);
                var addBody = mail.Value.MailType == MailType.Charged;

                character?.SendPacket(new SCGotMailPacket(mail.Value.Header, character.Mails.UnreadMailCount, addBody ? mail.Value.Body : null));
                mail.Value.IsDelivered = true;
            }
        }
        return tempMails;
    }

    public bool NotifyNewMailByNameIfOnline(BaseMail m, string receiverName)
    {
        Logger.Trace($"NotifyNewMailByNameIfOnline() - {receiverName}");
        // If unread and ready to deliver
        if (m.Header.Status != MailStatus.Read && m.Body.RecvDate <= DateTime.UtcNow && m.IsDelivered == false)
        {
            var player = worldManager.GetCharacter(receiverName);
            if (player != null)
            {
                // TODO: Mia mail stuff
                var addBody = m.MailType == MailType.Charged;
                player.Mails.UnreadMailCount.AddTotal(m.MailType);
                player.Mails.UnreadMailCount.UpdateReceived(m.MailType, 1);

                player.SendPacket(new SCGotMailPacket(m.Header, player.Mails.UnreadMailCount, addBody ? m.Body : null));
                // Charged mail only publishes the goods-mailbox event. The portrait
                // envelope listens to the normal inbox event, so close the inbox
                // listing (kind 1) to refresh that icon after a shop delivery.
                if (m.MailType is MailType.Charged or MailType.Promotion)
                    player.SendPacket(new SCMailListEndPacket(1, player.Mails.UnreadMailCount));
                m.IsDelivered = true;
                return true;
            }
        }
        return false;
    }

    public bool NotifyDeleteMailByNameIfOnline(BaseMail m, string receiverName)
    {
        Logger.Trace($"NotifyDeleteMailByNameIfOnline() - {receiverName}");
        var player = worldManager.GetCharacter(receiverName);
        if (player != null)
        {
            if (m.Header.Status != MailStatus.Read)
                player.Mails.UnreadMailCount.UpdateReceived(m.MailType, -1);
            player.Mails.UnreadMailCount.AddTotal(m.MailType, -1);
            player.SendPacket(new SCMailDeletedPacket(false, m.Id, true, player.Mails.UnreadMailCount));
            return true;
        }
        return false;
    }

    public void CheckAllMailTimings()
    {
        // Deliver yet "undelivered" mails
        Logger.Trace("CheckAllMailTimings");
        var undeliveredMails = _allPlayerMails.Where(x => x.Value.Body.RecvDate <= DateTime.UtcNow && x.Value.IsDelivered == false).ToDictionary(x => x.Key, x => x.Value);
        var delivered = 0;
        foreach (var mail in undeliveredMails)
            if (NotifyNewMailByNameIfOnline(mail.Value, mail.Value.Header.ReceiverName))
                delivered++;
        if (delivered > 0)
            Logger.Debug($"{delivered}/{undeliveredMails.Count} mail(s) delivered");

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

        // mailId arrives from the client, so confirm the bill is actually addressed to the payer.
        if (mail.Header.ReceiverId != character.Id)
        {
            Logger.Warn($"{character.Name} ({character.Id}) tried to settle mail {mailId}, addressed to {mail.Header.ReceiverId}");
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
        var houseZoneGroup = (mail.Header.Extra >> 48) & 0xFFFF; // Extract zone group Id from Extra
        var house = housingManager.Value.GetHouseById(houseId);

        if (house == null)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        if (FeaturesManager.Fsets.TaxItem)
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
                if (userBoundTaxCount > 0 && c > 0)
                {
                    if (c > userBoundTaxCount)
                        c = userBoundTaxCount;
                    character.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.Mail, Item.BoundTaxCertificate, c, null);
                    consumedCerts -= c;
                }
                c = consumedCerts;
                if (userTaxCount > 0 && c > 0)
                {
                    if (c > userTaxCount)
                        c = userTaxCount;
                    character.Inventory.Bag.ConsumeItem(Models.Game.Items.Actions.ItemTaskType.Mail, Item.TaxCertificate, c, null);
                    consumedCerts -= c;
                }

                if (consumedCerts != 0)
                    Logger.Error("Something went wrong when paying tax for mailId {0}", mail.Id);

                mail.Body.BillingAmount = consumedCerts;

            }
        }
        else
        {
            var billingBalance = autoUseAAPoint ? character.AaPoint : character.Money;
            if (mail.Body.BillingAmount > billingBalance)
            {
                character.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return false;
            }

            var paid = autoUseAAPoint
                ? character.SubtractAAPoint(SlotType.Inventory, mail.Body.BillingAmount, ItemTaskType.Mail)
                : character.SubtractMoney(SlotType.Inventory, mail.Body.BillingAmount, ItemTaskType.Mail);
            if (!paid)
                return false;
        }

        if (!housingManager.Value.PayWeeklyTax(house))
            Logger.Error("Could not update protection time when paying taxes, mailId {0}", mail.Id);
        else
        {
            if (mail.Header.Status != MailStatus.Read)
            {
                mail.Header.Status = MailStatus.Read;
                character.Mails.UnreadMailCount.UpdateReceived(mail.MailType, -1);
            }

            character.SendPacket(new SCChargeMoneyPaidPacket(mail.Id));
            character.SendPacket(new SCMailDeletedPacket(false, mail.Id, false, character.Mails.UnreadMailCount));
            DeleteMail(mail);
            character.Mails.SendUnreadMailCount();
        }

        return true;
    }

    public static void ExtractExtraForHouse(long extra, out ushort zoneGroupId, out uint houseId)
    {
        houseId = (uint)(extra & 0xFFFFFFFF); // Extract house DB Id from Extra
        zoneGroupId = (ushort)((extra >> 48) & 0xFFFF); // Extract zone group Id from Extra
    }

    public void DeleteHouseMails(uint houseId)
    {
        var deleteList = new List<long>();
        // Check which mails to remove
        foreach (var m in _allPlayerMails)
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
        foreach (var m in _allPlayerMails)
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

    public List<BaseMail> CreateQuestRewardMails(ICharacter character, Quest quest, List<ItemCreationDefinition> itemCreationDefinitions, int mailCopper)
    {
        var resultList = new List<BaseMail>();

        MailPlayerToPlayer mail = null;
        var questName = localizationManager.Get("quest_contexts", "name", quest.TemplateId, quest.TemplateId.ToString());

        // Generate a finalized list of all reward items in the mail attachments container of the player
        var totalRewardsItemsList = new List<Item>();
        foreach (var item in itemCreationDefinitions)
        {
            var itemTemplate = itemManager.GetTemplate(item.TemplateId);
            var itemGrade = itemTemplate.FixedGrade;
            if (itemGrade <= 0)
                itemGrade = 0;
            if (item.GradeId > 0)
                itemGrade = item.GradeId;

            character.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid, item.TemplateId, item.Count,
                itemGrade, out var newItemsList, out _, 0, -1);

            foreach (var newItem in newItemsList)
            {
                totalRewardsItemsList.Add(newItem);
            }
        }

        // Distribute the quest rewards
        foreach (var item in totalRewardsItemsList)
        {
            if (mail == null || mail.Body.Attachments.Count >= 10)
            {
                mail = new MailPlayerToPlayer(character, character.Name)
                {
                    Header = { SenderId = 0, SenderName = ".questReward" }, MailType = MailType.SysExpress, // NOTE: On newer versions, this uses the .title / .body format, but this doesn't seem to work on 1.2
                    // mail.Title = $".title('{questName}')";
                    // mail.Body.Text = $".body('{questName}')";
                    Title = questName,
                    Body = { Text = $"Reward for quest {questName}.", CopperCoins = mailCopper }
                };
                mailCopper = 0;
                resultList.Add(mail);
            }

            mail.Body.Attachments.Add(item);
        }

        foreach (var baseMail in resultList)
        {
            (baseMail as MailPlayerToPlayer)?.FinalizeAttachments();
        }

        return resultList;
    }
}
