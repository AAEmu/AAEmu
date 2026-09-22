using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// <c>indun_action_send_mail_rewards</c> (2 rows): action 361 (zone group 146, kind 6 dungeon_difficult)
/// and 408 (zone group 158, kind 7 soldier_rank), kinds from <c>enum_instance_reward_kinds</c>. The
/// reward rows themselves are <c>instance_rewards</c> (241 rows) and <c>instance_reward_mail_texts</c>,
/// which the server does not load yet, so this action only claims the once-per-copy grant and logs;
/// the mail is the instance_rewards feature's to send through that claim.
/// </summary>
internal class IndunActionSendMailReward : IndunAction
{
    public uint InstanceRewardKindId { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        var dungeon = worldInstance?.DungeonInstance;
        if (dungeon == null)
        {
            Logger.Debug($"IndunActionSendMailReward {Id}: world {worldInstance?.Id} is not a dungeon copy");
            return;
        }

        if (!dungeon.TryClaimMailReward(InstanceRewardKindId))
        {
            Logger.Debug($"IndunActionSendMailReward {Id}: reward kind {InstanceRewardKindId} already claimed in world {worldInstance.Id}");
            return;
        }

        Logger.Debug($"IndunActionSendMailReward {Id}: reward kind {InstanceRewardKindId} claimed for world {worldInstance.Id}; instance_rewards mail is not implemented, nothing sent");
    }
}
