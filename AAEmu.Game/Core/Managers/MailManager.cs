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
    private readonly Dictionary<long, BaseMail> _pendingMails = [];
    private List<long> _deletedMailIds = [];
    [ThreadStatic] private static List<(BaseMail Mail, int Stamp)> t_writtenMails;
    [ThreadStatic] private static List<long> t_deletedWritten;
    private readonly HashSet<long> _reservedMailIds = [];
    // Unused: private object _lock = new();

    public static int CostNormal { get; set; } = 50;
    public static int CostNormalAttachment { get; set; } = 30;
    public static int CostExpress { get; set; } = 100;
    public static int CostExpressAttachment { get; set; } = 80;
    public static int CostFreeAttachmentCount { get; set; } = 1;
    public static TimeSpan NormalMailDelay { get; set; } = TimeSpan.FromMinutes(30); // Default is 30 minutes

    // Unread/read retention is per mail type: see MailRetentionRules.
    /// <summary>The sender's Sent history keeps a letter this long, regardless of the receiver.</summary>
    public static TimeSpan SentMailExpiry { get; set; } = TimeSpan.FromDays(30);

    public BaseMail GetMailById(long id)
    {
        if (_allPlayerMails.TryGetValue(id, out var theMail) && MailDeliveryRules.IsPublished(theMail))
            return theMail;
        return null;
    }

    public uint GetNewMailId()
    {
        return GetNewMailId(out _);
    }

    private uint GetNewMailId(out bool reusedDeletedId)
    {
        lock (_deletedMailIds)
        {
            var Id = mailIdManager.GetNextId();
            reusedDeletedId = _deletedMailIds.Contains(Id);
            if (reusedDeletedId)
                _deletedMailIds.Remove(Id);
            return Id;
        }
    }

    public bool Send(BaseMail mail, bool publishNow = true)
    {
        if (!TryEnqueue(mail, out _, publishNow))
            return false;
        if (EnsurePersisted() != WorldSaveStatus.Failed)
            return true;

        DiscardUnpersisted(mail);
        return false;
    }

    /// <summary>
    /// Enqueues the letter and writes it on the caller's transaction so a claim/settlement
    /// marker cannot commit without the mail row.
    /// </summary>
    public bool TryDeliverOn(BaseMail mail, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (mail == null || connection == null || transaction == null)
            return false;

        if (!TryStageDelivery(mail, out _))
            return false;

        try
        {
            // Hold attachments off the periodic world save until PublishDelivered.
            MailDeliveryRules.HoldAttachmentsFromWorldSave(mail, true);
            MailDeliveryRules.PrepareAttachments(mail);
            WriteMail(mail, connection, transaction);
            PersistMailAttachments(mail, connection, transaction);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "TryDeliverOn failed for mail {0}", mail.Id);
            DiscardUnpersisted(mail);
            return false;
        }
    }

    /// <summary>
    /// Plans delivery of already-persistent item objects without changing their live owner,
    /// container, slots, counts, or ids before the caller's transaction commits.
    /// </summary>
    public bool TryCreateExistingItemDeliveryPlan(
        IReadOnlyList<Item> items,
        Func<int, IReadOnlyList<Item>, BaseMail> createMail,
        out ExistingItemMailDeliveryPlan plan)
    {
        plan = null;
        if (items == null || items.Count == 0 || createMail == null)
            return false;

        var itemIds = new HashSet<ulong>();
        var itemReferences = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        foreach (var item in items)
        {
            if (item is not { Id: > 0, Count: > 0 } ||
                !itemIds.Add(item.Id) ||
                !itemReferences.Add(item))
                return false;
        }

        var stagedBatches = new List<ExistingItemMailDeliveryBatch>();
        try
        {
            var batchIndex = 0;
            for (var offset = 0; offset < items.Count; offset += MailBody.MaxMailAttachments, batchIndex++)
            {
                var count = Math.Min(MailBody.MaxMailAttachments, items.Count - offset);
                var batchItems = new Item[count];
                for (var i = 0; i < count; i++)
                    batchItems[i] = items[offset + i];
                var readonlyBatch = Array.AsReadOnly(batchItems);

                var snapshots = new ItemPersistenceSnapshot[count];
                for (var i = 0; i < count; i++)
                    snapshots[i] = itemManager.CapturePersistenceSnapshot(batchItems[i]);

                var mail = createMail(batchIndex, readonlyBatch);
                if (mail?.Header == null || mail.Body == null || mail.Id != 0 ||
                    mail.Body.Attachments.Count != 0)
                    throw new InvalidOperationException("Existing-item mail factories must return a new attachment-empty mail.");

                for (var i = 0; i < snapshots.Length; i++)
                {
                    snapshots[i].ValidateLiveState();
                    snapshots[i] = snapshots[i].WithLocation(0, SlotType.Mail, i, mail.Header.ReceiverId);
                    mail.Body.Attachments.Add(batchItems[i]);
                }
                mail.Header.Attachments = mail.GetTotalAttachmentCount();

                if (!TryStageDeliveryCore(mail, out _, out var reusedDeletedMailId))
                {
                    ReleaseUnstagedExistingItemMail(mail, reusedDeletedMailId);
                    throw new InvalidOperationException("The existing-item mail receiver could not be verified.");
                }

                stagedBatches.Add(new ExistingItemMailDeliveryBatch(
                    mail,
                    Array.AsReadOnly(snapshots),
                    reusedDeletedMailId));
            }

            plan = new ExistingItemMailDeliveryPlan(this, stagedBatches.AsReadOnly());
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to create an existing-item mail delivery plan");
            foreach (var batch in stagedBatches)
                RollbackStagedExistingItemMail(batch);
            return false;
        }
    }

    internal bool TryPersistExistingItemDelivery(
        ExistingItemMailDeliveryPlan plan,
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        if (plan == null || connection == null || transaction == null)
            return false;

        try
        {
            var snapshots = new List<ItemPersistenceSnapshot>();
            foreach (var batch in plan.Batches)
            {
                ValidateStagedExistingItemBatch(batch, verifyReceiver: true);
                snapshots.AddRange(batch.ItemSnapshots);
            }

            // Complete validation precedes the first database write, so a caller never receives
            // a partially-written plan merely because a later batch had changed.
            foreach (var batch in plan.Batches)
                WriteMail(batch.Mail, connection, transaction);

            var written = itemManager.PersistSnapshots(connection, transaction, snapshots);
            if (written != snapshots.Count)
                throw new GameException($"Existing-item mail plan persisted {written}/{snapshots.Count} item rows");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to persist an existing-item mail delivery plan");
            return false;
        }
    }

    internal void CommitExistingItemDelivery(ExistingItemMailDeliveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Validate the whole plan before the first live mutation. The caller holds the shared
        // inventory guard from capture through this post-commit apply.
        foreach (var batch in plan.Batches)
            ValidateStagedExistingItemBatch(batch, verifyReceiver: false);

        foreach (var batch in plan.Batches)
        {
            foreach (var snapshot in batch.ItemSnapshots)
                itemManager.ApplyCommittedSnapshot(snapshot);
            PublishDelivered(batch.Mail);
        }
    }

    internal void RollbackExistingItemDelivery(ExistingItemMailDeliveryPlan plan)
    {
        if (plan == null)
            return;
        foreach (var batch in plan.Batches)
            RollbackStagedExistingItemMail(batch);
    }

    private void ValidateStagedExistingItemBatch(ExistingItemMailDeliveryBatch batch, bool verifyReceiver)
    {
        if (batch.Mail.Body.Attachments.Count != batch.ItemSnapshots.Count ||
            batch.ItemSnapshots.Count is 0 or > MailBody.MaxMailAttachments ||
            batch.Mail.Header.Attachments != batch.Mail.GetTotalAttachmentCount() ||
            !batch.Mail.IsPendingPublish ||
            (verifyReceiver && !TryVerifyDeliveryTarget(batch.Mail, out _)))
            throw new InvalidOperationException($"Staged mail {batch.Mail.Id} no longer matches its delivery plan.");

        lock (_pendingMails)
        {
            if (!_pendingMails.TryGetValue(batch.Mail.Id, out var pending) || !ReferenceEquals(pending, batch.Mail))
                throw new InvalidOperationException($"Staged mail {batch.Mail.Id} is no longer pending.");
        }

        for (var i = 0; i < batch.ItemSnapshots.Count; i++)
        {
            var snapshot = batch.ItemSnapshots[i];
            if (!ReferenceEquals(batch.Mail.Body.Attachments[i], snapshot.Item))
                throw new InvalidOperationException($"Staged mail {batch.Mail.Id} attachment {i} changed.");
            snapshot.ValidateForPersistence();
            if (snapshot.Desired.ContainerId != 0 || snapshot.Desired.SlotType != SlotType.Mail ||
                snapshot.Desired.Slot != i || snapshot.Desired.OwnerId != batch.Mail.Header.ReceiverId)
                throw new InvalidOperationException($"Staged mail {batch.Mail.Id} attachment {i} has an invalid projection.");
        }
    }

    private void RollbackStagedExistingItemMail(ExistingItemMailDeliveryBatch batch)
    {
        var mail = batch.Mail;
        if (mail == null)
            return;

        var removed = false;
        lock (_pendingMails)
        {
            if (_pendingMails.TryGetValue(mail.Id, out var pending) && ReferenceEquals(pending, mail))
                removed = _pendingMails.Remove(mail.Id);
        }

        if (!removed)
            return;

        if (mail.Id is > 0 and <= uint.MaxValue)
        {
            lock (_deletedMailIds)
            {
                if (batch.RestoreDeletedMailIdOnRollback && !_deletedMailIds.Contains(mail.Id))
                    _deletedMailIds.Add(mail.Id);
                mailIdManager.ReleaseId((uint)mail.Id);
            }
        }

        mail.Body.Attachments.Clear();
        mail.Header.Attachments = mail.GetTotalAttachmentCount();
        mail.IsPendingPublish = false;
        mail.Id = 0;
    }

    private void ReleaseUnstagedExistingItemMail(BaseMail mail, bool restoreDeletedMailId)
    {
        if (mail?.Id is > 0 and <= uint.MaxValue)
        {
            lock (_deletedMailIds)
            {
                if (restoreDeletedMailId && !_deletedMailIds.Contains(mail.Id))
                    _deletedMailIds.Add(mail.Id);
                mailIdManager.ReleaseId((uint)mail.Id);
            }
        }

        if (mail?.Body != null)
        {
            mail.Body.Attachments.Clear();
            mail.Header.Attachments = mail.GetTotalAttachmentCount();
            mail.IsPendingPublish = false;
            mail.Id = 0;
        }
    }

    public bool SendBatch(IReadOnlyList<BaseMail> mails)
    {
        if (!TryPrepareBatch(mails, out var batch))
            return false;
        if (PublishPreparedBatch(batch))
        {
            PersistNow();
            return true;
        }

        CancelPreparedBatch(batch);
        return false;
    }

    public bool TryPrepareBatch(IReadOnlyList<BaseMail> mails, out PreparedMailBatch batch)
    {
        batch = null;
        if (mails == null || mails.Count == 0)
            return false;

        var receivers = new string[mails.Count];
        for (var i = 0; i < mails.Count; i++)
        {
            var mail = mails[i];
            if (mail == null)
                return false;

            mail.PrepareForSend();
            if (!TryVerifyReceiver(mail, out receivers[i]))
                return false;
        }

        var batchIds = new HashSet<long>();
        var generatedMailIds = new List<uint>();
        foreach (var mail in mails)
        {
            if (mail.Id <= 0)
            {
                Logger.Trace("SendBatch() - Assign new mail Id");
                mail.Id = GetNewMailId();
                generatedMailIds.Add((uint)mail.Id);
            }
            if (!batchIds.Add(mail.Id))
            {
                Logger.Error("SendBatch() - Duplicate mail {0} in batch", mail.Id);
                ReleaseGeneratedMailIds(mails, generatedMailIds);
                return false;
            }
        }

        lock (_allPlayerMails)
        {
            foreach (var mail in mails)
            {
                if (_allPlayerMails.ContainsKey(mail.Id) || _reservedMailIds.Contains(mail.Id))
                {
                    Logger.Error("SendBatch() - Refusing to replace existing mail {0}", mail.Id);
                    ReleaseGeneratedMailIds(mails, generatedMailIds);
                    return false;
                }
            }

            foreach (var mail in mails)
                _reservedMailIds.Add(mail.Id);
        }

        batch = new PreparedMailBatch(mails.ToList(), receivers, generatedMailIds);
        return true;
    }

    public void PersistPreparedBatch(
        IReadOnlyList<BaseMail> mails,
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(mails);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        lock (_allPlayerMails)
        {
            if (mails.Count == 0 || mails.Any(mail =>
                    mail == null || !_reservedMailIds.Contains(mail.Id) || _allPlayerMails.ContainsKey(mail.Id)))
                throw new InvalidOperationException("Mail batch is not reserved for persistence");
        }

        foreach (var mail in mails)
        {
            MailDeliveryRules.PrepareAttachments(mail);
            WriteMail(mail, connection, transaction);
            PersistMailAttachments(mail, connection, transaction);
        }
    }

    public bool PublishPreparedBatch(PreparedMailBatch batch, bool alreadyPersisted = false)
    {
        if (batch == null || batch.IsCompleted)
            return false;

        lock (_allPlayerMails)
        {
            if (batch.Mails.Any(mail =>
                    !_reservedMailIds.Contains(mail.Id) || _allPlayerMails.ContainsKey(mail.Id)))
                return false;

            foreach (var mail in batch.Mails)
            {
                _allPlayerMails.Add(mail.Id, mail);
                _reservedMailIds.Remove(mail.Id);
                if (alreadyPersisted)
                {
                    mail.IsDirty = false;
                    foreach (var attachment in mail.Body.Attachments)
                        attachment.IsDirty = false;
                }
            }
            batch.IsCompleted = true;
        }

        for (var i = 0; i < batch.Mails.Count; i++)
        {
            try
            {
                NotifyNewMailByNameIfOnline(batch.Mails[i], batch.ReceiverNames[i]);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to notify receiver {0} for committed mail {1}",
                    batch.ReceiverNames[i], batch.Mails[i].Id);
            }
        }
        return true;
    }

    public void CancelPreparedBatch(PreparedMailBatch batch)
    {
        if (batch == null || batch.IsCompleted)
            return;

        lock (_allPlayerMails)
        {
            foreach (var mail in batch.Mails)
                _reservedMailIds.Remove(mail.Id);
            batch.IsCompleted = true;
        }
        ReleaseGeneratedMailIds(batch.Mails, batch.GeneratedMailIds);
    }

    private void ReleaseGeneratedMailIds(
        IReadOnlyList<BaseMail> mails,
        IReadOnlyCollection<uint> generatedMailIds)
    {
        foreach (var id in generatedMailIds)
            mailIdManager.ReleaseId(id);
        foreach (var mail in mails)
        {
            if (generatedMailIds.Contains((uint)mail.Id))
                mail.Id = 0;
        }
    }

    private bool TryVerifyReceiver(BaseMail mail, out string targetName)
    {
        targetName = nameManager.GetCharacterName(mail.Header.ReceiverId);
        var targetId = nameManager.GetCharacterId(mail.Header.ReceiverName);
        if (!string.Equals(targetName, mail.Header.ReceiverName, StringComparison.InvariantCultureIgnoreCase))
        {
            Logger.Debug("SendBatch() - Failed to verify receiver name {0} != {1}", targetName, mail.Header.ReceiverName);
            return false;
        }
        if (targetId != mail.Header.ReceiverId)
        {
            Logger.Debug("SendBatch() - Failed to verify receiver id {0} != {1}", targetId, mail.Header.ReceiverId);
            return false;
        }
        return true;
    }

    private void PersistMailAttachments(BaseMail mail, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (mail.Body.Attachments.Count == 0)
            return;

        foreach (var item in mail.Body.Attachments)
        {
            if (!MailDeliveryRules.CanPersistAttachment(item))
                throw new GameException($"Mail {mail.Id} attachment {item?.Id} is not owned by a mail slot");
        }

        var written = itemManager.PersistMailAttachments(mail.Body.Attachments, connection, transaction);
        if (written != mail.Body.Attachments.Count)
            throw new GameException($"Mail {mail.Id} persisted {written}/{mail.Body.Attachments.Count} attachments");
    }

    public void DiscardUnpersisted(BaseMail mail)
    {
        if (mail == null)
            return;
        lock (_pendingMails)
            _pendingMails.Remove(mail.Id);
        lock (_allPlayerMails)
            _allPlayerMails.Remove(mail.Id);
        foreach (var item in mail.Body.Attachments)
        {
            if (item?.Id > 0)
                itemManager.ReleaseId(item.Id);
        }
        mail.Body.Attachments.Clear();
        mail.IsPendingPublish = false;
        mail.IsDirty = true;
    }

    /// <summary>
    /// Assigns an id and holds the letter off the mailbox until <see cref="PublishDelivered"/>.
    /// </summary>
    public bool TryStageDelivery(BaseMail mail, out string targetName)
    {
        return TryStageDeliveryCore(mail, out targetName, out _);
    }

    private bool TryStageDeliveryCore(BaseMail mail, out string targetName, out bool reusedDeletedMailId)
    {
        if (!TryAssignDelivery(mail, out targetName, out reusedDeletedMailId))
            return false;

        mail.IsPendingPublish = true;
        lock (_pendingMails)
        lock (_allPlayerMails)
        {
            if (_pendingMails.ContainsKey(mail.Id) || _allPlayerMails.ContainsKey(mail.Id))
            {
                Logger.Error("TryStageDelivery() - Refusing to replace existing mail {0}", mail.Id);
                mail.IsPendingPublish = false;
                return false;
            }

            _pendingMails.Add(mail.Id, mail);
        }

        return true;
    }

    public void PublishDelivered(BaseMail mail)
    {
        if (mail == null)
            return;

        string receiverName;
        lock (_pendingMails)
            _pendingMails.Remove(mail.Id);

        mail.IsPendingPublish = false;
        MailDeliveryRules.HoldAttachmentsFromWorldSave(mail, false);
        _allPlayerMails ??= [];
        lock (_allPlayerMails)
        {
            _allPlayerMails[mail.Id] = mail;
        }

        receiverName = nameManager.GetCharacterName(mail.Header.ReceiverId) ?? mail.Header.ReceiverName;
        try
        {
            NotifyNewMailByNameIfOnline(mail, receiverName);
        }
        catch (Exception ex)
        {
            // The letter is already published and the caller already committed.
            // Do not throw: callers must not treat this as a delivery rollback.
            Logger.Error(ex, "PublishDelivered notify failed for mail {0}", mail.Id);
        }
    }

    private bool TryEnqueue(BaseMail mail, out string targetName, bool publishNow = true)
    {
        if (!TryAssignDelivery(mail, out targetName))
            return false;

        _allPlayerMails ??= [];
        lock (_allPlayerMails)
        {
            if (_allPlayerMails.ContainsKey(mail.Id))
            {
                Logger.Error("TryEnqueue() - Refusing to replace existing mail {0}", mail.Id);
                return false;
            }

            // publishNow=false keeps the letter out of player reads (GetMailById, mail lists) until
            // PublishDelivered, but it still belongs in _allPlayerMails: the world save has to write
            // it in the same snapshot that charges the sender, or a crash loses a paid-for letter.
            mail.IsPendingPublish = !publishNow;
            _allPlayerMails.Add(mail.Id, mail);
        }

        if (publishNow)
            NotifyNewMailByNameIfOnline(mail, targetName);

        return true;
    }

    private bool TryAssignDelivery(BaseMail mail, out string targetName)
    {
        return TryAssignDelivery(mail, out targetName, out _);
    }

    private bool TryAssignDelivery(BaseMail mail, out string targetName, out bool reusedDeletedMailId)
    {
        reusedDeletedMailId = false;
        if (!TryVerifyDeliveryTarget(mail, out targetName))
            return false;

        if (mail.Id <= 0)
            mail.Id = GetNewMailId(out reusedDeletedMailId);

        // Retention stamps for every mail, whichever path created it (Send or TryDeliverOn).
        return true;
    }

    private bool TryVerifyDeliveryTarget(BaseMail mail, out string targetName)
    {
        targetName = nameManager.GetCharacterName(mail.Header.ReceiverId);
        var targetId = nameManager.GetCharacterId(mail.Header.ReceiverName);
        if (!string.Equals(targetName, mail.Header.ReceiverName, StringComparison.InvariantCultureIgnoreCase))
        {
            Logger.Debug("TryAssignDelivery() - Failed to verify receiver name {0} != {1}", targetName, mail.Header.ReceiverName);
            return false;
        }
        if (targetId != mail.Header.ReceiverId)
        {
            Logger.Debug("TryAssignDelivery() - Failed to verify receiver id {0} != {1}", targetId, mail.Header.ReceiverId);
            return false;
        }
        return true;
    }

    public bool TryReturnToSender(BaseMail mail)
    {
        return TryReturnToSenderCore(mail, null);
    }

    /// <summary>
    /// Expiry return: bypasses the player-facing eligibility test so system, housing and
    /// auction mail can hand their attachments back to the sender instead of destroying them.
    /// </summary>
    public bool TryReturnExpiredToSender(BaseMail mail)
    {
        return TryReturnToSenderCore(mail, null, true);
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
    private bool TryReturnToSenderCore(BaseMail mail, uint? authorizedReceiverId, bool ignoreEligibility = false)
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

            if (!ignoreEligibility)
            {
                var eligible = authorizedReceiverId.HasValue
                    ? mail.CanBeReturnedBy(authorizedReceiverId.Value)
                    : mail.CanReturnMail();
                if (!eligible)
                    return false;
            }

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
            // The returned letter is a fresh delivery to the original sender. Without this its old
            // RecvDate leaves it expired already, so the sweep would bounce it straight back again.
            mail.Body.RecvDate = DateTime.UtcNow;
        }

        var originalReceiver = worldManager.GetCharacterById(originalReceiverId);
        if (originalReceiver is { IsOnline: true })
        {
            // The client's SCMailReturned reader (FUN_39a9f110) expects a CountUnreadMail after the
            // header; refresh so the toast carries current counters.
            originalReceiver.Mails.RefreshAllMailCounts();
            originalReceiver.SendPacket(new SCMailReturnedPacket(mail.Id, mail.Header, originalReceiver.Mails.UnreadMailCount));
        }

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

    /// <summary>Frees everything a mail still holds (items, coin, AA). Used by logical delete/expiry.</summary>
    public void ReleaseMailAttachments(BaseMail mail)
    {
        if (mail == null)
            return;
        for (var i = mail.Body.Attachments.Count - 1; i >= 0; i--)
        {
            var item = mail.Body.Attachments[i];
            if (item == null)
                continue;
            if (item._holdingContainer != null)
                item._holdingContainer.RemoveItem(ItemTaskType.Invalid, item, true);
            else
                itemManager.ReleaseId(item.Id);
        }
        mail.Body.Attachments.Clear();
        mail.Header.Attachments = 0;
        mail.Body.CopperCoins = 0;
        mail.Body.BillingAmount = 0;
        mail.Body.MoneyAmount2 = 0;
        mail.IsDirty = true;
    }

    /// <summary>Receiver-side delete or 3-day expiry: hide the letter, free its contents.</summary>
    public void DeleteForReceiver(BaseMail mail)
    {
        if (mail == null || mail.ReceiverDeleted)
            return;
        mail.ReceiverDeleted = true;
        ReleaseMailAttachments(mail);
        if (mail.SenderDeleted)
            DeleteMail(mail.Id);
        else
            PersistNow();
    }

    /// <summary>Sender-side delete or 30-day expiry of their Sent history.</summary>
    public void DeleteForSender(BaseMail mail)
    {
        if (mail == null || mail.SenderDeleted)
            return;
        mail.SenderDeleted = true;
        if (mail.ReceiverDeleted)
            DeleteMail(mail.Id);
        else
            PersistNow();
    }

    public bool DeleteMail(long id)
    {
        bool removed;
        lock (_allPlayerMails)
            removed = _allPlayerMails.Remove(id);

        // DeleteFor* and the retention sweep can both reach the same mail, and _deletedMailIds is
        // cleared once the delete is persisted. The tracked-map removal is the authoritative
        // once-only guard: a second ReleaseId would hand the id to a new mail while this row is pending.
        if (!removed)
            return false;

        lock (_deletedMailIds)
        {
            if (!_deletedMailIds.Contains(id))
                _deletedMailIds.Add(id);
            mailIdManager.ReleaseId((uint)id);
        }

        PersistNow();
        return true;
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

                        tempMail.SenderDeleted = reader.GetInt32("sender_deleted") != 0;
                        tempMail.ReceiverDeleted = reader.GetInt32("receiver_deleted") != 0;

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

                t_deletedWritten = [.. _deletedMailIds];
            }
        }

        foreach (var mtbs in _allPlayerMails)
        {
            // A letter a caller stages with TryStageDelivery lives in _pendingMails and is written by
            // that caller's own transaction. Anything in this dictionary is ours to write, including
            // a letter still hidden from players: hidden must not mean non-durable.
            if (!mtbs.Value.TryCaptureDirtyStamp(out var stamp))
                continue;
            WriteMail(mtbs.Value, connection, transaction);
            t_writtenMails ??= [];
            t_writtenMails.Add((mtbs.Value, stamp));
            updatedCount++;
        }

        return (updatedCount, deletedCount);
    }

    private static void WriteMail(BaseMail mail, MySqlConnection connection, MySqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandText = "REPLACE INTO mails(" +
            "`id`,`type`,`status`,`title`,`text`,`sender_id`,`sender_name`," +
            "`attachment_count`,`receiver_id`,`receiver_name`,`open_date`,`send_date`,`received_date`," +
            "`sender_deleted`,`receiver_deleted`," +
            "`returned`,`extra`,`money_amount_1`,`money_amount_2`,`money_amount_3`," +
            "`attachment0`,`attachment1`,`attachment2`,`attachment3`,`attachment4`,`attachment5`," +
            "`attachment6`,`attachment7`,`attachment8`,`attachment9`" +
            ") VALUES (" +
            "@id, @type, @status, @title, @text, @senderId, @senderName, " +
            "@attachment_count, @receiverId, @receiverName, @openDate, @sendDate, @receivedDate, " +
            "@sender_deleted, @receiver_deleted, " +
            "@returned, @extra, @money1, @money2, @money3," +
            "@attachment0, @attachment1, @attachment2, @attachment3, @attachment4, @attachment5, " +
            "@attachment6, @attachment7, @attachment8, @attachment9" +
            ")";

        command.Parameters.AddWithValue("@id", mail.Id);
        command.Parameters.AddWithValue("@openDate", mail.Header.OpenDate);
        command.Parameters.AddWithValue("@type", (byte)mail.Header.Type);
        command.Parameters.AddWithValue("@status", mail.Header.Status);
        command.Parameters.AddWithValue("@title", mail.Header.Title);
        command.Parameters.AddWithValue("@text", mail.Body.Text);
        command.Parameters.AddWithValue("@senderId", mail.Header.SenderId);
        command.Parameters.AddWithValue("@senderName", mail.Header.SenderName);
        command.Parameters.AddWithValue("@attachment_count", mail.Header.Attachments);
        command.Parameters.AddWithValue("@receiverId", mail.Header.ReceiverId);
        command.Parameters.AddWithValue("@receiverName", mail.Header.ReceiverName);
        command.Parameters.AddWithValue("@sendDate", mail.Body.SendDate);
        command.Parameters.AddWithValue("@receivedDate", mail.Body.RecvDate);
        command.Parameters.AddWithValue("@sender_deleted", mail.SenderDeleted ? 1 : 0);
        command.Parameters.AddWithValue("@receiver_deleted", mail.ReceiverDeleted ? 1 : 0);
        command.Parameters.AddWithValue("@returned", mail.Header.Returned ? 1 : 0);
        command.Parameters.AddWithValue("@extra", mail.Header.Extra);
        command.Parameters.AddWithValue("@money1", mail.Body.CopperCoins);
        command.Parameters.AddWithValue("@money2", mail.Body.BillingAmount);
        command.Parameters.AddWithValue("@money3", mail.Body.MoneyAmount2);

        for (var i = 0; i < MailBody.MaxMailAttachments; i++)
        {
            if (i >= mail.Body.Attachments.Count)
                command.Parameters.AddWithValue("@attachment" + i.ToString(), 0);
            else
                command.Parameters.AddWithValue("@attachment" + i.ToString(), mail.Body.Attachments[i].Id);
        }

        command.Prepare();
        if (command.ExecuteNonQuery() < 1)
            throw new InvalidOperationException($"Mail {mail.Id} was not written");
    }

    public void ConfirmSaved()
    {
        if (t_writtenMails != null)
        {
            foreach (var (mail, stamp) in t_writtenMails)
                mail.TryClearDirty(stamp);
        }

        if (t_deletedWritten != null)
        {
            lock (_deletedMailIds)
            {
                foreach (var id in t_deletedWritten)
                    _deletedMailIds.Remove(id);
            }
        }

        t_writtenMails = null;
        t_deletedWritten = null;
    }

    public void DiscardPendingClears()
    {
        t_writtenMails = null;
        t_deletedWritten = null;
    }

    [ThreadStatic] private static int t_persistDeferDepth;
    [ThreadStatic] private static bool t_persistRequested;
    [ThreadStatic] private static WorldSaveStatus t_lastFlushStatus;

    /// <summary>
    /// Marks a money operation. Holds every <see cref="PersistNow"/> request made on this
    /// thread until the outermost scope is disposed, and holds <see cref="PersistenceGate"/>
    /// shared so no save on any thread snapshots the operation halfway. A money operation that
    /// sends mail (player mail with coin, an outbid refund, a buyout settle) then reaches the
    /// database as one snapshot taken after all of its balance, item, lot and mail mutations,
    /// instead of a save issued from inside <see cref="Send"/> that still shows the sender's
    /// pre-charge balance or the bid that was just refunded.
    /// Open the scope before taking any lock a save also takes (the house lock, for one).
    /// </summary>
    public IDisposable DeferPersist()
    {
        if (t_persistDeferDepth == 0)
            PersistenceGate.EnterOperation();
        t_persistDeferDepth++;
        return new PersistScope(this);
    }

    /// <summary>
    /// Writes dirty mail (and the rest of the World snapshot) immediately, or at the end of
    /// the enclosing <see cref="DeferPersist"/> scope.
    /// Claim/send/delete used to wait for the 5-minute tick; a killed World
    /// then reloaded the pre-claim row and the letter came back unclaimed.
    /// No-ops in tests that do not register <see cref="ISaveManager"/>.
    /// </summary>
    public void PersistNow() => _ = EnsurePersisted();

    /// <summary>Status of the last flush on this thread, then resets to <see cref="WorldSaveStatus.Saved"/>.</summary>
    public WorldSaveStatus TakeLastFlushStatus()
    {
        var status = t_lastFlushStatus;
        t_lastFlushStatus = WorldSaveStatus.Saved;
        return status;
    }

    /// <summary>
    /// Writes a requested snapshot now while staying inside <see cref="DeferPersist"/>.
    /// Outermost scopes release the gate only for the write (a save needs it exclusively).
    /// Nested scopes leave the request for the outer dispose and report
    /// <see cref="WorldSaveStatus.Busy"/>.
    /// </summary>
    public WorldSaveStatus FlushRequestedNow() => FlushRequestedNow(null);

    public WorldSaveStatus FlushRequestedNow(Action onFailed)
    {
        if (t_persistDeferDepth > 1)
            return WorldSaveStatus.Busy;

        if (t_persistDeferDepth == 0)
            return FlushPersist(onFailed);

        if (!t_persistRequested)
        {
            t_lastFlushStatus = WorldSaveStatus.Saved;
            return WorldSaveStatus.Saved;
        }

        t_persistRequested = false;
        PersistenceGate.ExitOperation();
        try
        {
            return FlushPersist(onFailed);
        }
        finally
        {
            PersistenceGate.EnterOperation();
        }
    }

    private WorldSaveStatus EnsurePersisted()
    {
        if (t_persistDeferDepth > 0)
        {
            t_persistRequested = true;
            return WorldSaveStatus.Saved;
        }

        return FlushPersist();
    }

    private WorldSaveStatus FlushPersist(Action onFailed = null)
    {
        var saver = SingletonContainer.ServiceProvider?.GetService<ISaveManager>();
        if (saver == null)
        {
            t_lastFlushStatus = WorldSaveStatus.Saved;
            return WorldSaveStatus.Saved;
        }

        // A save that is already running took the gate after this operation released it, so
        // it carries everything the operation wrote. Nothing is lost by not saving twice.
        var status = saver.TrySave(onFailed);
        t_lastFlushStatus = status;
        if (status == WorldSaveStatus.Busy)
            Logger.Debug("Mail persist folded into the save already in progress");
        return status;
    }

    private sealed class PersistScope(MailManager owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (t_persistDeferDepth > 0)
                t_persistDeferDepth--;
            if (t_persistDeferDepth > 0)
                return;

            // Release the gate before saving: the save needs it exclusively.
            PersistenceGate.ExitOperation();
            if (!t_persistRequested)
            {
                t_lastFlushStatus = WorldSaveStatus.Saved;
                return;
            }

            t_persistRequested = false;
            owner.FlushPersist();
        }
    }

    #endregion

    
    public Dictionary<long, BaseMail> GetCurrentMailList(uint characterId)
    {
        // Try to grab the actual online Character object to send live updates
        var character = worldManager.GetCharacterById(characterId);
        var tempMails = _allPlayerMails.Where(
            x => MailDeliveryRules.IsPublished(x.Value) &&
                 ((x.Value.Header.ReceiverId == characterId &&
                   !x.Value.ReceiverDeleted &&
                   x.Value.Body.RecvDate <= DateTime.UtcNow) ||
                  (x.Value.Header.SenderId == characterId &&
                   !x.Value.SenderDeleted))
                 ).
            ToDictionary(x => x.Key, x => x.Value);
        character?.Mails.UnreadMailCount.ResetReceived();
        character?.Mails.UnreadMailCount.ResetTotals();
        // Pre-pass: total counts every landed received mail (read + unread) so each pushed SCGotMail carries
        // the final total (the client uses the total, not unread, to size each mail-list tab).
        foreach (var mail in tempMails)
        {
            if (mail.Value.Header.ReceiverId == characterId && !mail.Value.ReceiverDeleted)
                character?.Mails.UnreadMailCount.AddTotal(mail.Value.MailType);
        }
        foreach (var mail in tempMails)
        {
            //if ((mail.Value.Header.Status != MailStatus.Read) && (mail.Value.Header.SenderId != character.Id))
            if (mail.Value.Header.ReceiverId == characterId && !mail.Value.ReceiverDeleted &&
                mail.Value.Header.Status != MailStatus.Read)
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

        // Never announce a letter players cannot see: a caller that staged it has not committed yet,
        // and notifying would both leak the delivery and bump the unread counters if the save fails.
        if (!MailDeliveryRules.IsPublished(m))
            return false;

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
        // Skip staged mail: a letter held back until its save commits must not be delivered here, or
        // a failed send leaves phantom mail and inflated unread counters behind.
        var undeliveredMails = _allPlayerMails.Where(x => MailDeliveryRules.IsPublished(x.Value) &&
            x.Value.Body.RecvDate <= DateTime.UtcNow && x.Value.IsDelivered == false).ToDictionary(x => x.Key, x => x.Value);
        var delivered = 0;
        foreach (var mail in undeliveredMails)
            if (NotifyNewMailByNameIfOnline(mail.Value, mail.Value.Header.ReceiverName))
                delivered++;
        if (delivered > 0)
            Logger.Debug($"{delivered}/{undeliveredMails.Count} mail(s) delivered");

        // Retention sweep at most once per 5 minutes (delivery above stays on the 5s tick).
        var now = DateTime.UtcNow;
        if (now - _lastRetentionSweep < TimeSpan.FromMinutes(5))
            return;
        _lastRetentionSweep = now;
        ExpireDueMails(now);
    }

    private DateTime _lastRetentionSweep = DateTime.MinValue;

    /// <summary>
    /// Retail retention: read mail is kept MailRetentionRules.ReadRetention, unread mail its
    /// per-type window, and demolition notices are kept regardless of read state.
    /// </summary>
    private static bool IsReceiverExpired(BaseMail mail, DateTime now)
    {
        var type = mail.Header.Type;
        if (MailRetentionRules.IgnoresReadState(type))
            return mail.Body.RecvDate + MailRetentionRules.DemolitionRetention <= now;

        if (mail.Header.Status == MailStatus.Read)
        {
            var readAt = mail.Header.OpenDate == default ? mail.Body.RecvDate : mail.Header.OpenDate;
            return readAt + MailRetentionRules.ReadRetention <= now;
        }

        return mail.Body.RecvDate + MailRetentionRules.UnreadRetention(type) <= now;
    }

    /// <summary>
    /// Sender: Sent history expires 30 days after send, regardless of the receiver.
    /// A row is removed only once both sides are gone.
    /// </summary>
    private void ExpireDueMails(DateTime now)
    {
        var changed = false;
        foreach (var (id, mail) in _allPlayerMails.ToList())
        {
            if (!mail.ReceiverDeleted && IsReceiverExpired(mail, now))
            {
                var receiver = worldManager.GetCharacterById(mail.Header.ReceiverId);
                var wasUnread = mail.Header.Status != MailStatus.Read;

                // Unread mail hands its attachments back to the sender; read mail's are deleted.
                // The return flips the letter in place (the original receiver becomes the sender),
                // so the copy to drop is the flip's sender side. DeleteForReceiver here would free
                // the returned attachments and hide the letter from the player it was returned to.
                // A returned letter is never returned a second time: it already travelled back once,
                // so returning it again would bounce it between the two mailboxes every window.
                if (wasUnread && !mail.Header.Returned && TryReturnExpiredToSender(mail))
                    DeleteForSender(mail);
                else if (!mail.ReceiverDeleted)
                    DeleteForReceiver(mail);

                receiver?.SendPacket(new SCMailDeletedPacket(false, id, wasUnread, receiver.Mails.UnreadMailCount));
                changed = true;
            }

            if (!mail.SenderDeleted &&
                mail.Body.SendDate + SentMailExpiry <= now)
            {
                DeleteForSender(mail);
                changed = true;
            }

            if (mail.SenderDeleted && mail.ReceiverDeleted)
                DeleteMail(id);
        }

        if (changed)
            Logger.Debug("Mail retention sweep applied");
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
            // Derived from the issued bill, exactly as X2House:CountTaxItemForTax derives it from the
            // money amount. The bill already carries the heavy-property modifier applied at issuance,
            // so the certificate path and the money path settle the same recorded obligation.
            var consumedCerts = Math.Max(1, (int)Math.Ceiling(mail.Body.BillingAmount / 10000f));

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
