using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Claims the account-return reward: an exactly-once ledger insert plus the reward grant on the same
/// transaction (see <see cref="AccountReturnManager.TryClaim"/>). The packet has no body.
/// </summary>
/// <remarks>
/// Every parameterless C2S type folds onto that one read function, so the shared address is
/// identical-COMDAT folding, not a base-class fall-through. The reward item type comes from
/// <c>content_configs return_account_reward_item_type</c> (enum 275) resolved through
/// <c>const_item_types</c>; a missing row refuses the grant loudly instead of falling back.
/// </remarks>
public class CSTakeReturnAccountItemPacket() : GamePacket(CSOffsets.CSTakeReturnAccountItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var connection = Connection;
        var character = connection.ActiveChar;
        if (character == null)
        {
            Logger.Error("TakeReturnAccountItem: no active character; refusing the claim");
            return;
        }

        PreparedMailBatch batch = null;
        var result = AccountReturnManager.Instance.TryClaim(connection.AccountId, (db, tx) =>
        {
            var mail = BuildRewardMail(character);
            if (mail == null)
                return false;
            if (!MailManager.Instance.TryPrepareBatch([mail], out batch))
            {
                Logger.Error("TakeReturnAccountItem: mail batch refused for account {0}",
                    connection.AccountId);
                return false;
            }

            MailManager.Instance.PersistPreparedBatch([mail], db, tx);
            return true;
        });

        if (result == AccountReturnClaimResult.Claimed && batch != null)
            MailManager.Instance.PublishPreparedBatch(batch, alreadyPersisted: true);
        else if (batch != null)
            MailManager.Instance.CancelPreparedBatch(batch);

        Logger.Info("TakeReturnAccountItem account {0}: {1}", connection.AccountId, result);

        // Answer with the availability the client should show now: taken -> false, refused -> re-read.
        connection.SendPacket(new SCReturnAccountStatusPacket(
            result == AccountReturnClaimResult.Claimed
                ? false
                : AccountReturnManager.Instance.IsRewardAvailable(connection.AccountId)));
    }

    private static BaseMail BuildRewardMail(Character character)
    {
        var rewardType = (uint)ReturnAccountRules.RewardItemType;
        var itemId = ItemManager.Instance.GetConstItemIdByType(rewardType);
        if (itemId == 0)
        {
            Logger.Error(
                "TakeReturnAccountItem: const_item_types row '{0}' behind '{1}' is missing; no reward granted",
                rewardType, ReturnAccountRules.RewardItemTypeKey);
            return null;
        }

        var template = ItemManager.Instance.GetTemplate(itemId);
        if (template == null)
        {
            Logger.Error("TakeReturnAccountItem: item template {0} behind type '{1}' is missing",
                itemId, ReturnAccountRules.RewardItemTypeKey);
            return null;
        }

        var grade = template.FixedGrade > 0 ? (byte)template.FixedGrade : (byte)0;
        var item = ItemManager.Instance.Create(itemId, 1, grade);
        item.OwnerId = character.Id;
        item.SlotType = SlotType.Mail;

        var mail = new BaseMail
        {
            MailType = MailType.Normal,
            Title = "Return Account Reward",
            ReceiverName = character.Name,
            Header =
            {
                SenderId = 0, SenderName = "System", ReceiverId = character.Id, Extra = 0
            },
            Body =
            {
                Text = string.Empty,
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow,
            }
        };
        mail.Body.Attachments.Add(item);
        return mail;
    }
}
