using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;

using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Re-rolls one reopen box: spends one free or paid open from the pack's content budget,
/// enforces the pack's reopen cooldown, draws a new reward and acknowledges with
/// <see cref="SCReopenRandomBoxRefreshPacket"/>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: bool isCharge, u64 itemId, u32 type. itemId is the box item
/// instance the state is keyed by; type carries the merchant_reopen_packs row the box draws
/// from.
/// </remarks>
public class CSReopenRandomBoxRefreshPacket() : GamePacket(CSOffsets.CSReopenRandomBoxRefreshPacket, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public bool IsCharge { get; private set; }
    public long ItemId { get; private set; }
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        IsCharge = stream.ReadBoolean();
        ItemId = stream.ReadInt64();
        TypeValue = stream.ReadInt32();
        HandleRefresh();
    }

    private void HandleRefresh()
    {
        if (Connection?.ActiveChar is not { } character)
            return;

        if (TypeValue <= 0)
        {
            Logger.Warn("Reopen box refresh: character {0} item {1} names no reopen pack (type {2})",
                character.Id, ItemId, TypeValue);
            return;
        }

        var packId = (uint)TypeValue;
        var pack = ReopenBoxManager.Instance.TryGetPack(packId);
        try
        {
            var result = ReopenBoxManager.Instance.TryRefresh(
                character.Id, ItemId, packId, IsCharge, DateTime.UtcNow,
                IsCharge ? () => pack != null && ChargeOpen(character, pack) : null);

            if (result == ReopenRefreshResult.Refreshed)
            {
                Connection.SendPacket(new SCReopenRandomBoxRefreshPacket(0));
                return;
            }

            // Failure codes for this family are not decoded in the corpus: say it in the log and
            // send nothing rather than invent an ErrorMessage value.
            Logger.Warn("Reopen box refresh refused for character {0} item {1} pack {2}: {3}",
                character.Id, ItemId, packId, result);
        }
        catch (RandomMerchantContentException ex)
        {
            Logger.Error(ex, "Reopen box refresh refused for character {0} item {1} pack {2}",
                character.Id, ItemId, packId);
        }
    }

    /// <summary>
    /// Takes the paid-open price in the pack's content currency: charge_point of currency_id,
    /// consumed as charge_item_id when that currency is item points.
    /// </summary>
    private static bool ChargeOpen(Character character, MerchantReopenPack pack)
    {
        if (pack.ChargePoint <= 0)
            return true; // content prices it at zero - nothing is charged

        if (pack.Currency == ContentCurrencyType.ItemPoint)
            return character.Inventory.Bag.ConsumeItem(
                       ItemTaskType.StoreBuy, pack.ChargeItemId, pack.ChargePoint, null) == pack.ChargePoint;

        return character.TryPayCurrency((uint)pack.Currency, pack.ChargePoint, false, ItemTaskType.StoreBuy);
    }
}
