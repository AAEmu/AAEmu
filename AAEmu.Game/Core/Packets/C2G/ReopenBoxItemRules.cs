using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Whether a bag item is the box that opens a given reopen pack.</summary>
internal static class ReopenBoxItemRules
{
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

    public static void MailExpiredRoll(Character character, ReopenBoxState state)
    {
        if (character == null || state == null || state.RewardItemId == 0 || state.RewardCount <= 0)
            return;
        var reward = ItemManager.Instance.Create(state.RewardItemId, state.RewardCount, state.RewardGrade);
        var mail = MailForReopenBox.ForExpiredRoll(
            character.Id, character.Name, state.PackId, state.GoodId, reward);
        MailManager.Instance.Send(mail);
    }
}
