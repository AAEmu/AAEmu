using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Skills.Effects;

using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Whether a bag item is the box that opens a given reopen pack.</summary>
internal static class ReopenBoxItemRules
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>Why a rolled reward cannot be delivered as written.</summary>
    internal enum ReopenRewardRefusal
    {
        /// <summary>The count is deliverable.</summary>
        None = 0,

        /// <summary>No item was rolled, so there is nothing to give.</summary>
        NoRewardItem,

        /// <summary>A count of zero or less is not a quantity anything can hold.</summary>
        NotPositive,

        /// <summary>
        /// The count is larger than one stack of the rolled item can hold, so it cannot be delivered as
        /// the single item both claim paths create.
        /// </summary>
        OverStackSize,

        /// <summary>No bound is known for the rolled item, and an unknown bound is not a satisfied one.</summary>
        NoTemplate
    }

    /// <summary>Whether a rolled reward can be delivered, reading the bound from content.</summary>
    internal static ReopenRewardRefusal CheckReward(uint rewardItemId, int rewardCount)
    {
        if (rewardItemId == 0)
            return ReopenRewardRefusal.NoRewardItem;

        var template = ItemManager.Instance.GetTemplate(rewardItemId);
        if (template == null)
            return ReopenRewardRefusal.NoTemplate;

        return CheckRewardCount(rewardItemId, rewardCount, template.MaxCount);
    }

    /// <summary>
    /// The decision itself, as a pure function of the roll and the authored bound.
    /// </summary>
    /// <param name="rewardItemId">The rolled item.</param>
    /// <param name="rewardCount">The rolled count.</param>
    /// <param name="maxStackSize">
    /// The rolled item's <c>max_stack_size</c>, or a non-positive value when it is unknown.
    /// </param>
    /// <remarks>
    /// The bound is read from content like everything else on this path - the shipped catalogue carries
    /// 27 distinct stack sizes. A count above it is refused rather than rounded down to fit: handing
    /// out less than the roll promised is worse than refusing it, and the row is left in place rather
    /// than consumed.
    /// </remarks>
    internal static ReopenRewardRefusal CheckRewardCount(uint rewardItemId, int rewardCount, int maxStackSize)
    {
        if (rewardItemId == 0)
            return ReopenRewardRefusal.NoRewardItem;

        if (rewardCount <= 0)
            return ReopenRewardRefusal.NotPositive;

        if (maxStackSize <= 0)
            return ReopenRewardRefusal.NoTemplate;

        return rewardCount > maxStackSize
            ? ReopenRewardRefusal.OverStackSize
            : ReopenRewardRefusal.None;
    }

    /// <summary>
    /// The other half of the transaction: what happens to the item and the letter once the write has
    /// either committed or not.
    /// </summary>
    /// <param name="committed">
    /// <c>true</c> once the row is durable, <c>false</c> on any path that abandoned the attempt.
    /// </param>
    /// <remarks>
    /// A commit is only half the delivery. <c>PublishPersistedItems</c> is what the world save needs
    /// before it will keep the item, and <c>PublishDelivered</c> is what puts the letter in the mailbox
    /// the client reads - so a commit without both leaves a reward that exists in the database and
    /// nowhere else, until a restart. An abandoned attempt needs the mirror image: the unpersisted item
    /// holds a reserved id and a row, so discarding it is what stops both leaking.
    /// <para>
    /// Both branches are told apart in one method rather than inlined at the call sites, because the two
    /// were previously asymmetric - the commit branch had no publish at all - and only a test that can
    /// call this can hold them to being the same shape.
    /// </para>
    /// </remarks>
    internal static void FinishDelivery(
        IItemManager itemManager, IMailManager mailManager, Item reward, BaseMail mail, bool committed)
    {
        try
        {
            if (committed)
            {
                itemManager.PublishPersistedItems([reward]);
                mailManager.PublishDelivered(mail);
            }
            else
            {
                // The reward is the letter's attachment, and DiscardUnpersisted releases every
                // attachment still in the mail - so calling both releases the same reserved id twice.
                // Exactly one path releases it, and the item manager covers only the case where the
                // letter was never built and so carries nothing.
                if (mail != null)
                    mailManager.DiscardUnpersisted(mail);
                else
                    itemManager.DiscardUnpersistedItems([reward]);
            }
        }
        catch (Exception ex)
        {
            // Best effort: the caller's own outcome is the thing being reported, and a throw here would
            // mask it. On the committed path a failed publish still leaves the row, so the error names
            // the mail rather than being swallowed.
            Logger.Error(ex, committed
                ? "Reopen box: reward {0} was committed but could not be published"
                : "Reopen box: could not release an undelivered reward", mail?.Id);
        }
    }

    public static bool OpensPack(Item box, uint packId)
    {
        if (box?.Template == null || box.Template.UseSkillId == 0)
            return false;
        var skill = SkillManager.Instance.GetSkillTemplate(box.Template.UseSkillId);
        if (skill == null)
            return false;
        foreach (var effect in skill.Effects)
        {
            if (effect.Template is GainMerchantReopenPackItemEffect reopen &&
                reopen.MerchantReopenPackId == packId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Takes one box, then writes the expired reward and deletes the state row together.
    /// Null means the roll was kept. False means the box is gone. True means a stack remains.
    /// </summary>
    public static bool? SettleExpired(Character character, ReopenBoxState state)
    {
        if (character == null || state == null || state.ItemId < 0)
            return null;

        var box = character.Inventory.GetItemById((ulong)state.ItemId);
        if (box == null || !OpensPack(box, state.PackId))
            return null;
        var templateId = box.TemplateId;
        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillEffectGainItem, templateId, 1, box) != 1)
            return null;

        if (!MailAndDelete(character, state))
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, templateId, 1, -1);
            Logger.Error("Reopen box: expired roll for item {0} was not mailed; the box unit was returned", state.ItemId);
            return null;
        }

        return character.Inventory.GetItemById((ulong)state.ItemId) != null;
    }

    private static bool MailAndDelete(Character character, ReopenBoxState state)
    {
        // The same bound the claim path asks, so a roll neither path can deliver is refused by both.
        var refusal = CheckReward(state.RewardItemId, state.RewardCount);
        if (refusal != ReopenRewardRefusal.None)
        {
            Logger.Error(
                "Reopen box: expired roll for item {0} cannot be delivered ({1}: item {2}, count {3})",
                state.ItemId, refusal, state.RewardItemId, state.RewardCount);
            return false;
        }

        var reward = ItemManager.Instance.CreateUnpersisted(state.RewardItemId, state.RewardCount, state.RewardGrade);
        var mail = MailForReopenBox.ForExpiredRoll(
            character.Id, character.Name, state.PackId, state.GoodId, reward);
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            if (!MailManager.Instance.TryDeliverOn(mail, connection, transaction))
            {
                transaction.Rollback();
                FinishDelivery(ItemManager.Instance, MailManager.Instance, reward, mail, committed: false);
                return false;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "DELETE FROM character_reopen_boxes WHERE character_id = @character_id AND item_id = @item_id";
            command.Parameters.AddWithValue("@character_id", character.Id);
            command.Parameters.AddWithValue("@item_id", state.ItemId);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: expired roll mail failed for item {0}", state.ItemId);
            FinishDelivery(ItemManager.Instance, MailManager.Instance, reward, mail, committed: false);
            return false;
        }

        // Committed. The item becomes live and the letter joins the mailbox, in that order: publishing
        // the letter first would let a client see a mail whose attachment is not yet real.
        FinishDelivery(ItemManager.Instance, MailManager.Instance, reward, mail, committed: true);
        return true;
    }
}
