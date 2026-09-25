using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.World;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum IndunRewardDeliveryResult
{
    Delivered,
    AlreadyClaimed,
    NoRecipients,
    SelectionUnavailable,
    Failed
}

internal enum ClaimCommitState
{
    NotCommitted,
    Committed,
    Unknown
}

/// <summary>
/// W03A delivery for indun mail rewards with an evidence-backed selection value. The claim row and
/// the mail rows are written on one MySQL transaction, so a retry after an ambiguous commit cannot
/// create a second letter. W03B's <c>instance_reward_bonus_counts</c> is deliberately not read here.
/// </summary>
public sealed class IndunRewardDeliveryService : Singleton<IndunRewardDeliveryService>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Func<MySqlConnection> _openConnection;
    private readonly IMailManager _mailManager;
    private readonly IItemManager _itemManager;
    private readonly Action<MySqlTransaction> _commit;

    public IndunRewardDeliveryService()
        : this(MySQL.CreateConnection, MailManager.Instance, ItemManager.Instance)
    {
    }

    internal IndunRewardDeliveryService(
        Func<MySqlConnection> openConnection,
        IMailManager mailManager,
        IItemManager itemManager,
        Action<MySqlTransaction> commit = null)
    {
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _mailManager = mailManager ?? throw new ArgumentNullException(nameof(mailManager));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _commit = commit ?? (transaction => transaction.Commit());
    }

    public IndunRewardDeliveryResult Deliver(WorldInstance world, uint instanceRewardKindId, int selectionValue)
    {
        var dungeon = world?.DungeonInstance;
        if (dungeon == null)
        {
            Logger.Error("Instance reward delivery has no dungeon world");
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        var instanceId = dungeon.GetInstanceCatalogId;
        if (instanceId == 0)
        {
            Logger.Error("Instance reward delivery has no instance catalog id for zone group {0}", dungeon.GetZoneGroupId);
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        if (!IndunGameData.Instance.TryGetAuthoredDifficultySelection(
                instanceId, instanceRewardKindId, dungeon.Difficult, out var authoredSelection) ||
            authoredSelection != selectionValue)
        {
            Logger.Error("Instance reward delivery rejected an unauthored or non-difficulty selection value {0} for instance {1}, kind {2}",
                selectionValue, instanceId, instanceRewardKindId);
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        IReadOnlyList<InstanceReward> rewards;
        InstanceRewardMailText mailText;
        try
        {
            rewards = IndunGameData.Instance.GetInstanceRewards(instanceId, instanceRewardKindId, selectionValue);
            mailText = IndunGameData.Instance.GetInstanceRewardMailText(instanceId);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            Logger.Error(ex, "Instance reward content is not deliverable for instance {0}, kind {1}, value {2}",
                instanceId, instanceRewardKindId, selectionValue);
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        var recipients = world.GetAllCharacters()
            .Where(character => character != null)
            .GroupBy(character => character.Id)
            .Select(group => group.First())
            .OrderBy(character => character.Id)
            .Select(character => new IndunRewardRecipient(character.Id, character.Name))
            .ToArray();
        return DeliverForRun(dungeon.RewardRunId, instanceId, instanceRewardKindId, selectionValue,
            recipients, rewards, mailText);
    }

    /// <summary>
    /// Transactional core used by the action and by the opt-in MySQL integration tests. The run id
    /// is supplied by the caller; production callers must pass the same persisted logical run id when
    /// a copy is rehydrated. A fresh process has no automatic dungeon-run recovery in this slice.
    /// </summary>
    internal IndunRewardDeliveryResult DeliverForRun(
        string runId,
        uint instanceId,
        uint instanceRewardKindId,
        int selectionValue,
        IReadOnlyList<IndunRewardRecipient> recipients,
        IReadOnlyList<InstanceReward> rewards,
        InstanceRewardMailText mailText)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId != runId.Trim() || runId.Length > 128 ||
            instanceId == 0 || instanceRewardKindId == 0 ||
            recipients == null || rewards == null || mailText == null || mailText.InstanceId != instanceId ||
            mailText.MailKindId == 0 || string.IsNullOrWhiteSpace(mailText.MailSender) ||
            string.IsNullOrWhiteSpace(mailText.MailTitle) || string.IsNullOrWhiteSpace(mailText.MailBody) ||
            rewards.Any(reward => reward.InstanceId != instanceId || reward.InstanceRewardKindId != instanceRewardKindId) ||
            recipients.Any(recipient => recipient.Id == 0 || string.IsNullOrWhiteSpace(recipient.Name)))
        {
            Logger.Error("Instance reward delivery has an invalid durable run/content context");
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        var selectedRewards = rewards
            .Where(reward => selectionValue >= reward.StartRange && selectionValue <= reward.EndRange)
            .ToArray();
        if (selectedRewards.Length == 0)
        {
            Logger.Error("Instance reward delivery has no authored row for run {0}, instance {1}, kind {2}, value {3}",
                runId, instanceId, instanceRewardKindId, selectionValue);
            return IndunRewardDeliveryResult.SelectionUnavailable;
        }

        if (recipients.Count == 0)
            return IndunRewardDeliveryResult.NoRecipients;

        var delivered = 0;
        var failed = 0;
        foreach (var recipient in recipients)
        {
            var result = DeliverOne(runId, instanceId, instanceRewardKindId, selectionValue,
                recipient, selectedRewards, mailText);
            switch (result)
            {
                case IndunRewardDeliveryResult.Delivered:
                    delivered++;
                    break;
                case IndunRewardDeliveryResult.AlreadyClaimed:
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (failed > 0)
            return IndunRewardDeliveryResult.Failed;
        return delivered > 0 ? IndunRewardDeliveryResult.Delivered : IndunRewardDeliveryResult.AlreadyClaimed;
    }

    private IndunRewardDeliveryResult DeliverOne(
        string runId,
        uint instanceId,
        uint instanceRewardKindId,
        int selectionValue,
        IndunRewardRecipient recipient,
        InstanceReward[] rewards,
        InstanceRewardMailText mailText)
    {
        if (rewards.Any(reward => reward.RewardAmount <= 0))
        {
            Logger.Error("Instance reward {0}/{1} contains a non-positive amount; no zero-reward fallback is defined",
                instanceId, instanceRewardKindId);
            return IndunRewardDeliveryResult.Failed;
        }

        if (rewards.Length > MailBody.MaxMailAttachments)
        {
            Logger.Error("Instance reward {0}/{1} has {2} attachments; a single W03A mail supports {3}",
                instanceId, instanceRewardKindId, rewards.Length, MailBody.MaxMailAttachments);
            return IndunRewardDeliveryResult.Failed;
        }

        MySqlConnection connection = null;
        MySqlTransaction transaction = null;
        var createdItems = new List<Item>();
        BaseMail mail = null;
        var commitAttempted = false;
        try
        {
            connection = _openConnection();
            transaction = connection.BeginTransaction();
            if (!TryInsertClaim(connection, transaction, runId, instanceId, instanceRewardKindId, recipient.Id))
            {
                TryRollback(transaction);
                return IndunRewardDeliveryResult.AlreadyClaimed;
            }

            mail = BuildMail(recipient, mailText, rewards, createdItems);
            if (!_mailManager.TryDeliverOn(mail, connection, transaction))
            {
                TryRollback(transaction);
                // This is safe after TryDeliverOn's own cleanup too: DiscardUnpersisted
                // releases only attachments still present in the mail.
                _mailManager.DiscardUnpersisted(mail);
                return IndunRewardDeliveryResult.Failed;
            }

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"UPDATE indun_reward_claims
                                         SET mail_id=@mail_id
                                     WHERE run_id=@run_id
                                       AND instance_id=@instance_id
                                       AND instance_reward_kind_id=@kind_id
                                       AND character_id=@character_id";
                update.Parameters.AddWithValue("@mail_id", mail.Id);
                update.Parameters.AddWithValue("@run_id", runId);
                update.Parameters.AddWithValue("@instance_id", instanceId);
                update.Parameters.AddWithValue("@kind_id", instanceRewardKindId);
                update.Parameters.AddWithValue("@character_id", recipient.Id);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Instance reward claim disappeared before mail commit");
            }

            commitAttempted = true;
            _commit(transaction);
            transaction = null;
            try
            {
                _mailManager.PublishDelivered(mail);
            }
            catch (Exception ex)
            {
                // The mail and claim are already committed. Never turn a notification failure
                // into an in-memory rollback that would release a committed item id.
                Logger.Error(ex, "Instance reward mail {0} committed but publish notification failed", mail.Id);
            }
            return IndunRewardDeliveryResult.Delivered;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Instance reward delivery failed for character {0}, instance {1}, kind {2}, value {3}",
                recipient.Id, instanceId, instanceRewardKindId, selectionValue);
            if (commitAttempted && mail != null)
            {
                TryRollback(transaction, quiet: true);
                transaction = null;
                connection?.Dispose();
                connection = null;
                var commitState = TryResolveCommitState(runId, instanceId, instanceRewardKindId, recipient.Id, mail.Id);
                if (commitState == ClaimCommitState.Committed)
                {
                    transaction = null;
                    PublishRecovered(mail);
                    return IndunRewardDeliveryResult.Delivered;
                }

                if (commitState == ClaimCommitState.NotCommitted)
                {
                    TryRollback(transaction);
                    _mailManager.DiscardUnpersisted(mail);
                    return IndunRewardDeliveryResult.Failed;
                }

                // The commit outcome and the durable row cannot both be read. Keep the staged
                // objects for process-level recovery rather than releasing a possibly committed id.
                Logger.Error("Instance reward commit outcome is unknown for run {0}, character {1}; leaving delivery staged",
                    runId, recipient.Id);
                return IndunRewardDeliveryResult.Failed;
            }

            TryRollback(transaction);
            if (mail != null)
                _mailManager.DiscardUnpersisted(mail);
            else
                _itemManager.DiscardUnpersistedItems(createdItems);
            return IndunRewardDeliveryResult.Failed;
        }
        finally
        {
            transaction?.Dispose();
            connection?.Dispose();
        }
    }

    private BaseMail BuildMail(
        IndunRewardRecipient recipient,
        InstanceRewardMailText text,
        IReadOnlyList<InstanceReward> rewards,
        List<Item> createdItems)
    {
        var now = DateTime.UtcNow;
        var mail = new BaseMail
        {
            MailType = IndunRewardMailKindRules.Map(text.MailKind),
            ReceiverName = recipient.Name,
            Title = text.MailTitle,
            Header =
            {
                SenderId = 0,
                SenderName = text.MailSender,
                ReceiverId = recipient.Id,
                Status = MailStatus.Unread
            },
            Body =
            {
                Text = text.MailBody,
                SendDate = now,
                RecvDate = now
            }
        };

        foreach (var reward in rewards)
        {
            if (reward.UseGameScore || reward.GiveIgnoreVisitedCount || reward.ApplyConfig)
                throw new InvalidOperationException("W03A does not yet interpret instance reward option flags");
            if (reward.RewardTargetType != InstanceRewardTargetType.Item)
                throw new InvalidOperationException($"W03A does not yet map reward target type {reward.RewardTargetType}");

            var template = _itemManager.GetTemplate(reward.RewardTargetId) ??
                throw new InvalidDataException($"instance_rewards target item {reward.RewardTargetId} is missing");
            if (template.FixedGrade < 0)
                throw new InvalidDataException($"instance_rewards target item {reward.RewardTargetId} has no fixed grade");
            var grade = checked((byte)template.FixedGrade);
            var item = _itemManager.Create(reward.RewardTargetId, reward.RewardAmount, grade) ??
                throw new InvalidOperationException($"Failed to create instance reward item {reward.RewardTargetId}");
            item.OwnerId = recipient.Id;
            item.SlotType = SlotType.Mail;
            createdItems.Add(item);
            mail.Body.Attachments.Add(item);
        }

        return mail;
    }

    private void PublishRecovered(BaseMail mail)
    {
        try
        {
            _mailManager.PublishDelivered(mail);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Recovered instance reward mail {0} could not be published", mail?.Id);
        }
    }

    private ClaimCommitState TryResolveCommitState(
        string runId,
        uint instanceId,
        uint instanceRewardKindId,
        uint characterId,
        long mailId)
    {
        if (mailId <= 0)
            return ClaimCommitState.Unknown;

        try
        {
            using var connection = _openConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT mail_id
                                    FROM indun_reward_claims
                                    WHERE run_id=@run_id
                                      AND instance_id=@instance_id
                                      AND instance_reward_kind_id=@kind_id
                                      AND character_id=@character_id";
            command.Parameters.AddWithValue("@run_id", runId);
            command.Parameters.AddWithValue("@instance_id", instanceId);
            command.Parameters.AddWithValue("@kind_id", instanceRewardKindId);
            command.Parameters.AddWithValue("@character_id", characterId);
            var value = command.ExecuteScalar();
            if (value is null or DBNull)
                return ClaimCommitState.NotCommitted;
            return Convert.ToInt64(value) == mailId ? ClaimCommitState.Committed : ClaimCommitState.Unknown;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not resolve the instance reward commit outcome for run {0}", runId);
            return ClaimCommitState.Unknown;
        }
    }

    private static bool TryInsertClaim(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string runId,
        uint instanceId,
        uint instanceRewardKindId,
        uint characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // All catalog values are validated before this point. INSERT IGNORE therefore
        // distinguishes only the duplicate ledger key (0) from a real insert (1), even
        // when the connection was opened with CLIENT_FOUND_ROWS.
        command.CommandText = @"INSERT IGNORE INTO indun_reward_claims
                                     (run_id, instance_id, instance_reward_kind_id, character_id, claimed_at)
                                 VALUES (@run_id, @instance_id, @kind_id, @character_id, UTC_TIMESTAMP(6))";
        command.Parameters.AddWithValue("@run_id", runId);
        command.Parameters.AddWithValue("@instance_id", instanceId);
        command.Parameters.AddWithValue("@kind_id", instanceRewardKindId);
        command.Parameters.AddWithValue("@character_id", characterId);
        return command.ExecuteNonQuery() == 1;
    }

    private static void TryRollback(MySqlTransaction transaction, bool quiet = false)
    {
        try
        {
            transaction?.Rollback();
        }
        catch (Exception ex)
        {
            if (!quiet)
                Logger.Error(ex, "Instance reward transaction rollback failed");
        }
    }
}
