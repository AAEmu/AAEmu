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
        if (state.RewardItemId == 0 || state.RewardCount <= 0)
            return false;

        var reward = ItemManager.Instance.Create(state.RewardItemId, state.RewardCount, state.RewardGrade);
        var mail = MailForReopenBox.ForExpiredRoll(
            character.Id, character.Name, state.PackId, state.GoodId, reward);
        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            if (!MailManager.Instance.TryDeliverOn(mail, connection, transaction))
            {
                transaction.Rollback();
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
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box: expired roll mail failed for item {0}", state.ItemId);
            return false;
        }
    }
}
