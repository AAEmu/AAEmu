using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using NLog;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// Owns every mail and fresh item created for one cart until the database transaction commits.
/// Nothing is published while the purchase is still reversible.
/// </summary>
public sealed class CashShopMailDelivery(IMailManager mailManager, IItemManager itemManager) : IDisposable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<BaseMail> _mails = [];
    private readonly List<Item> _unattachedItems = [];
    private bool _committed;
    private bool _disposed;

    public IReadOnlyList<BaseMail> Mails => _mails;

    public void AddMail(BaseMail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (_disposed)
            throw new ObjectDisposedException(nameof(CashShopMailDelivery));
        _mails.Add(mail);
    }

    public void AddUnattachedItems(IEnumerable<Item> items)
    {
        if (items == null)
            return;
        foreach (var item in items)
        {
            if (item != null)
                _unattachedItems.Add(item);
        }
    }

    public void MarkCommitted()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CashShopMailDelivery));
        _committed = true;
    }

    /// <summary>
    /// Transfers ownership immediately after the database commit, then applies live state and
    /// publishes mail. A post-commit failure can no longer make Dispose discard durable items.
    /// </summary>
    public void CompleteCommit(Action postCommit)
    {
        ArgumentNullException.ThrowIfNull(postCommit);
        MarkCommitted();
        try
        {
            postCommit();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Post-commit cash-shop state application failed");
        }
        finally
        {
            Publish();
        }
    }

    public void Publish()
    {
        if (!_committed || _disposed)
            return;
        foreach (var mail in _mails)
        {
            try
            {
                mailManager.PublishDelivered(mail);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not publish committed cash-shop mail {0}", mail.Id);
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

        foreach (var mail in _mails)
        {
            try
            {
                mailManager.DiscardUnpersisted(mail);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not discard uncommitted cash-shop mail {0}", mail.Id);
            }
        }

        foreach (var item in _unattachedItems)
        {
            try
            {
                if (item?.Id > 0 && ReferenceEquals(itemManager.GetItemByItemId(item.Id), item))
                    itemManager.ReleaseId(item.Id);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not release uncommitted cash-shop item {0}", item?.Id);
            }
        }
    }
}

public static class CashShopMailDeliveryFactory
{
    public static CashShopMailDelivery Create(
        CashShopPurchasePlan plan,
        Character buyer,
        Character target,
        IItemManager itemManager,
        IMailManager mailManager)
    {
        if (plan is null || buyer is null || target is null || itemManager is null || mailManager is null)
            throw new ArgumentException("Cash-shop delivery content and managers are required.");

        var created = new CashShopMailDelivery(mailManager, itemManager);
        var pending = new List<Item>();
        try
        {
            foreach (var line in plan.Lines)
            {
                pending.Clear();
                CreateItemStacks(itemManager, line.ItemId, line.ItemCount, pending);
                if (line.BonusItemId != 0)
                    CreateItemStacks(itemManager, line.BonusItemId, line.BonusItemCount, pending);
                if (pending.Count == 0)
                    throw new InvalidDataException($"Cash-shop SKU {line.SkuId} has no deliverable item content.");

                foreach (var chunk in pending.Chunk(MailBody.MaxMailAttachments))
                {
                    var attachments = chunk.ToArray();
                    var mail = new CommercialMail(
                        target.Id,
                        target.Name,
                        buyer.Name,
                        attachments.ToList(),
                        target.Id != buyer.Id,
                        false,
                        line.DeliveryTitle);
                    mail.PrepareForTransactionalDelivery();
                    created.AddMail(mail);
                    pending.RemoveAll(item => attachments.Contains(item, ReferenceEqualityComparer.Instance));
                }
            }

            return created;
        }
        catch (Exception ex)
        {
            created.AddUnattachedItems(pending);
            created.Dispose();
            throw new InvalidOperationException("Could not create the cash-shop delivery from loaded content.", ex);
        }
    }

    private static void CreateItemStacks(IItemManager itemManager, uint templateId, uint count, List<Item> output)
    {
        if (templateId == 0 || count == 0 || count > int.MaxValue)
            throw new InvalidDataException("Cash-shop item content is missing or has an invalid quantity.");

        var template = itemManager.GetTemplate(templateId);
        if (template is not { MaxCount: > 0 })
            throw new InvalidDataException($"Cash-shop item template {templateId} has no valid stack size.");

        var grade = template.FixedGrade >= 0
            ? checked((byte)template.FixedGrade)
            : (byte)ItemGrade.Crude;
        var remaining = checked((int)count);
        while (remaining > 0)
        {
            var amount = Math.Min(remaining, template.MaxCount);
            var item = itemManager.Create(templateId, amount, grade, true) ??
                       throw new InvalidOperationException($"Could not create cash-shop item {templateId}.");
            item.ExcludeFromWorldSave = true;
            output.Add(item);
            remaining -= amount;
        }
    }
}
