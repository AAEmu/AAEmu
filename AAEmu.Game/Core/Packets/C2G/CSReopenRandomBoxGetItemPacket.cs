using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;

using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Claims the reward currently rolled for one reopen box: the claim is taken durably first and
/// the granted exactly once, then acknowledged with <see cref="SCReopenRandomBoxGetItemPacket"/>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: the body is one record struct - four u32 fields all named "type"
/// (distinct fields the corpus does not further pin), s32 freeCnt, s32 chargeCnt, s32 lifeTime,
/// u64 itemId, u64 openDate, u64 refreshDate. The struct is the client's echo of the box record;
/// only the pack key (the first type) and the itemId are acted on, and the authoritative counters,
/// roll and dates are the server's own persisted state - the echoed numbers are parsed and
/// ignored rather than trusted.
/// </remarks>
public class CSReopenRandomBoxGetItemPacket() : GamePacket(CSOffsets.CSReopenRandomBoxGetItemPacket, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public int Type1 { get; private set; }
    public int Type2 { get; private set; }
    public int Type3 { get; private set; }
    public int Type4 { get; private set; }
    public uint FreeCnt { get; private set; }
    public uint ChargeCnt { get; private set; }
    public uint LifeTime { get; private set; }
    public long ItemId { get; private set; }
    public long OpenDate { get; private set; }
    public long RefreshDate { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type1 = stream.ReadInt32();
        Type2 = stream.ReadInt32();
        Type3 = stream.ReadInt32();
        Type4 = stream.ReadInt32();
        FreeCnt = stream.ReadUInt32();
        ChargeCnt = stream.ReadUInt32();
        LifeTime = stream.ReadUInt32();
        ItemId = stream.ReadInt64();
        OpenDate = stream.ReadInt64();
        RefreshDate = stream.ReadInt64();

        HandleClaim();
    }

    private void HandleClaim()
    {
        if (Connection?.ActiveChar is not { } character)
            return;

        var now = DateTime.UtcNow;
        try
        {
            var result = ReopenBoxManager.Instance.TryClaim(character.Id, ItemId, now,
                state => GrantReward(character, state));

            if (result == ReopenClaimResult.Claimed)
            {
                if (character.Inventory.GetItemById((ulong)ItemId) != null)
                    ReopenBoxManager.Instance.Forget(character.Id, ItemId);
                Connection.SendPacket(new SCReopenRandomBoxGetItemPacket(0));
                Connection.SendPacket(new SCReopenRandomBoxRemovePacket(ItemId, Type1));
                return;
            }

            // Failure codes for this family are not decoded in the corpus: say it in the log and
            // send nothing rather than invent an ErrorMessage value.
            Logger.Warn("Reopen box claim refused for character {0} item {1}: {2}",
                character.Id, ItemId, result);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Reopen box claim failed for character {0} item {1}", character.Id, ItemId);
        }
    }

    /// <summary>
    /// Delivers the rolled reward: to the bag when it fits, otherwise to the mail attachment
    /// container - the same delivery path the box's skill-effect grant uses, so a full inventory
    /// cannot swallow the reward.
    /// </summary>
    private static bool GrantReward(Character character, ReopenBoxState state)
    {
        if (state.RewardItemId == 0 || state.RewardCount <= 0)
        {
            Logger.Error(
                "Reopen box: character {0} item {1} has a roll with no deliverable reward (item {2}, count {3}) - refusing the claim",
                character.Id, state.ItemId, state.RewardItemId, state.RewardCount);
            return false;
        }

        var box = state.ItemId < 0 ? null : character.Inventory.GetItemById((ulong)state.ItemId);
        if (box == null || !ReopenBoxItemRules.OpensPack(box, state.PackId) ||
            character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillEffectGainItem, box.TemplateId, 1, box) != 1)
            return false;

        if (character.Inventory.Bag.SpaceLeftForItem(state.RewardItemId) >= state.RewardCount)
        {
            character.Inventory.Bag.AcquireDefaultItemEx(ItemTaskType.SkillEffectGainItem, state.RewardItemId,
                state.RewardCount, state.RewardGrade, out _, out _, character.Id);
        }
        else
        {
            character.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid, state.RewardItemId,
                state.RewardCount, state.RewardGrade, out _, out _, character.Id);
            character.SendErrorMessage(ErrorMessageType.BagFull);
        }

        return true;
    }
}
