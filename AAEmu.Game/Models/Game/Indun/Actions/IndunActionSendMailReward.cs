using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// W03A delivery hook for <c>indun_action_send_mail_rewards</c>. The current shipped selection
/// evidence is <c>instance_difficult_infos</c>; a kind whose selection source is not loaded fails
/// loudly instead of guessing a soldier-rank value. W03B bonus counts are intentionally excluded.
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

        var instanceId = dungeon.GetInstanceCatalogId;
        if (!IndunGameData.Instance.HasInstanceRewardKind(instanceId, InstanceRewardKindId) ||
            !IndunGameData.Instance.HasInstanceRewardMailText(instanceId))
        {
            Logger.Error("IndunActionSendMailReward {0}: missing instance_rewards or mail text join for instance {1}, kind {2}",
                Id, instanceId, InstanceRewardKindId);
            return;
        }

        if (!IndunGameData.Instance.TryGetAuthoredDifficultySelection(
                instanceId, InstanceRewardKindId, dungeon.Difficult, out var selectionValue))
        {
            var kindName = IndunGameData.Instance.GetInstanceRewardKindName(InstanceRewardKindId);
            Logger.Error("IndunActionSendMailReward {0}: unsupported non-difficulty or unauthored reward selection for instance {1}, kind {2} ({3}); soldier-rank values are not inferred",
                Id, instanceId, InstanceRewardKindId, kindName);
            return;
        }

        var result = IndunRewardDeliveryService.Instance.Deliver(worldInstance, InstanceRewardKindId, selectionValue);
        switch (result)
        {
            case IndunRewardDeliveryResult.Delivered:
            case IndunRewardDeliveryResult.AlreadyClaimed:
                if (!dungeon.TryClaimMailReward(InstanceRewardKindId))
                    Logger.Debug("IndunActionSendMailReward {0}: durable delivery completed before the in-memory copy marker", Id);
                break;
            case IndunRewardDeliveryResult.NoRecipients:
                Logger.Debug("IndunActionSendMailReward {0}: no recipients in world {1}", Id, worldInstance.Id);
                break;
            default:
                Logger.Error("IndunActionSendMailReward {0}: delivery did not complete ({1})", Id, result);
                break;
        }
    }
}
