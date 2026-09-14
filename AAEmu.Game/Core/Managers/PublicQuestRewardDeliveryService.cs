using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Quests;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed record PublicQuestRewardRecipient(uint CharacterId, string Name);

public interface IPublicQuestRewardDeliveryService
{
    bool TryStageRewards(
        PublicQuestRewardBundle bundle,
        IReadOnlyCollection<PublicQuestRewardRecipient> recipients,
        MySqlConnection connection,
        MySqlTransaction transaction,
        out PublicQuestStagedRewardDelivery delivery);
}

/// <summary>Stages public-assignment item rewards on the caller's completion transaction.</summary>
public sealed class PublicQuestRewardDeliveryService(
    IMailManager mailManager,
    IItemManager itemManager) : IPublicQuestRewardDeliveryService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public bool TryStageRewards(
        PublicQuestRewardBundle bundle,
        IReadOnlyCollection<PublicQuestRewardRecipient> recipients,
        MySqlConnection connection,
        MySqlTransaction transaction,
        out PublicQuestStagedRewardDelivery delivery)
    {
        delivery = null;
        if (bundle == null || recipients is not { Count: > 0 } || connection == null || transaction == null ||
            bundle.Items is not { Count: > 0 })
            return false;

        var recipientIds = new HashSet<uint>();
        var freshItems = new List<Item>();
        var mails = new List<BaseMail>();
        PublicQuestStagedRewardDelivery staged = null;
        try
        {
            foreach (var recipient in recipients)
            {
                if (recipient.CharacterId == 0 || string.IsNullOrWhiteSpace(recipient.Name) ||
                    !recipientIds.Add(recipient.CharacterId))
                    throw new InvalidDataException("Public quest reward recipients must be unique named characters.");

                var recipientItems = CreateItems(bundle.Items, freshItems);
                foreach (var batch in recipientItems.Chunk(MailBody.MaxMailAttachments))
                    mails.Add(CreateMail(bundle, recipient, batch));
            }

            staged = new PublicQuestStagedRewardDelivery(mailManager, itemManager, mails, freshItems);
            foreach (var mail in mails)
            {
                if (!mailManager.TryDeliverOn(mail, connection, transaction))
                {
                    Logger.Warn("Could not stage public quest reward mail for character {0}",
                        mail.Header.ReceiverId);
                    staged.Dispose();
                    return false;
                }
                staged.MarkStaged(mail);
            }

            delivery = staged;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not stage public quest reward bundle {0}", bundle.QuestTemplateId);
            if (staged != null)
                staged.Dispose();
            else
                ReleaseFreshItems(itemManager, freshItems);
            return false;
        }
    }

    private List<Item> CreateItems(IReadOnlyList<PublicQuestItemReward> rewards, List<Item> allFreshItems)
    {
        var result = new List<Item>();
        foreach (var reward in rewards)
        {
            if (reward.TemplateId == 0 || reward.Count <= 0)
                throw new InvalidDataException("Public quest item reward content is invalid.");
            var template = itemManager.GetTemplate(reward.TemplateId);
            if (template is not { MaxCount: > 0 })
                throw new InvalidDataException($"Public quest reward item {reward.TemplateId} has no valid stack size.");

            var remaining = reward.Count;
            while (remaining > 0)
            {
                var count = Math.Min(remaining, template.MaxCount);
                var item = itemManager.Create(reward.TemplateId, count, reward.GradeId) ??
                           throw new InvalidOperationException(
                               $"Could not create public quest reward item {reward.TemplateId}.");
                item.ExcludeFromWorldSave = true;
                result.Add(item);
                allFreshItems.Add(item);
                remaining -= count;
            }
        }
        return result;
    }

    private static BaseMail CreateMail(PublicQuestRewardBundle bundle, PublicQuestRewardRecipient recipient,
        IReadOnlyCollection<Item> attachments)
    {
        var now = DateTime.UtcNow;
        var mail = new BaseMail
        {
            MailType = MailType.SysExpress,
            ReceiverName = recipient.Name,
            Title = "Guild public assignment reward",
            Header =
            {
                Status = MailStatus.Unread,
                SenderId = 0,
                SenderName = ".questReward",
                ReceiverId = recipient.CharacterId
            },
            Body =
            {
                Text = $"Reward for guild public assignment {bundle.QuestName}.",
                SendDate = now,
                RecvDate = now
            }
        };
        mail.Body.Attachments.AddRange(attachments);
        return mail;
    }

    internal static void ReleaseFreshItems(IItemManager itemManager, IReadOnlyCollection<Item> freshItems)
    {
        foreach (var item in freshItems)
        {
            try
            {
                if (item?.Id > 0 && ReferenceEquals(itemManager.GetItemByItemId(item.Id), item))
                    itemManager.ReleaseId(item.Id);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not release fresh public quest reward item {0}", item?.Id);
            }
        }
    }
}

public sealed class PublicQuestStagedRewardDelivery : IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IMailManager _mailManager;
    private readonly IItemManager _itemManager;
    private readonly IReadOnlyList<BaseMail> _mails;
    private readonly IReadOnlyList<Item> _freshItems;
    private readonly List<BaseMail> _stagedMails = [];
    private readonly List<BaseMail> _publishedMails = [];
    private bool _committed;
    private bool _disposed;

    internal PublicQuestStagedRewardDelivery(IMailManager mailManager, IItemManager itemManager,
        IReadOnlyList<BaseMail> mails, IReadOnlyList<Item> freshItems)
    {
        _mailManager = mailManager;
        _itemManager = itemManager;
        _mails = mails;
        _freshItems = freshItems;
    }

    internal void MarkStaged(BaseMail mail) => _stagedMails.Add(mail);

    /// <summary>Call immediately after the caller-owned transaction commits.</summary>
    public void MarkCommitted() => _committed = true;

    /// <summary>Publishes committed mails to live mailboxes and online recipients.</summary>
    public void Publish()
    {
        if (!_committed)
            return;
        foreach (var mail in _mails)
        {
            if (_publishedMails.Contains(mail))
                continue;
            try
            {
                _mailManager.PublishDelivered(mail);
                _publishedMails.Add(mail);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not publish committed public quest reward mail {0}", mail.Id);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_committed)
            return;

        foreach (var mail in _stagedMails)
        {
            try
            {
                _mailManager.DiscardUnpersisted(mail);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not discard staged public quest reward mail {0}", mail?.Id);
            }
        }
        PublicQuestRewardDeliveryService.ReleaseFreshItems(_itemManager, _freshItems);
    }
}
