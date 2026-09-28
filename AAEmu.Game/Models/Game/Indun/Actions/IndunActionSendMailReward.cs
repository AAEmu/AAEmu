using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// W03A delivery hook for <c>indun_action_send_mail_rewards</c>.
/// <para>
/// W03C replaced the earlier difficulty-only gate with a three-way structural classification, because
/// the shipped content publishes three different kinds of selection evidence and they are not equally
/// blocked. <b>Difficulty-backed</b> is deliverable now. <b>Round-backed</b> has a proven selection
/// value but no authored trigger. <b>Rank-backed</b> has authored rank bands but no authored score to
/// order players with — it is refused for that reason alone, not because the kind is unauthored.
/// </para>
/// <para>
/// Typed bonus counts join only after this same selection succeeds.
/// </para>
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

        var verdict = IndunGameData.Instance.ClassifyInstanceRewardSelection(
            instanceId, InstanceRewardKindId, dungeon.Difficult);
        if (!verdict.DeliverableNow)
        {
            Logger.Error(
                "IndunActionSendMailReward {0}: {1} reward selection is not deliverable for instance {2} — {3}. {4}",
                Id,
                verdict.KindName,
                instanceId,
                verdict.Classification,
                InstanceRewardTaxonomyRules.DescribeBlocker(verdict));
            return;
        }

        var result = IndunRewardDeliveryService.Instance.Deliver(worldInstance, InstanceRewardKindId, verdict.SelectionValue);
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
