using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Manual refresh of the viewer's random shop window: spends one free or paid allowance from the
/// pack's content budget, re-rolls the window and acknowledges with
/// <see cref="SCRandomShopInfoRefreshPacket"/>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// field name alongside the value:
/// bool refreshFree, sbyte shopType, bc bc (3 bytes), int type
/// </remarks>
public class CSRandomShopInfoRefreshPacket() : GamePacket(CSOffsets.CSRandomShopInfoRefreshPacket, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public bool RefreshFree { get; private set; }
    public sbyte ShopType { get; private set; }
    public uint Bc { get; private set; }
    public int Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        RefreshFree = stream.ReadBoolean();
        ShopType = stream.ReadSByte();
        Bc = stream.ReadBc();
        Type = stream.ReadInt32();
        HandleRefresh();
    }

    private void HandleRefresh()
    {
        if (Connection?.ActiveChar is not { } character)
            return;

        var packId = character.ParentWorld?.GetNpc(Bc)?.Template?.MerchantRandomPackId ?? 0;
        if (packId == 0)
        {
            Logger.Warn("Random shop refresh: npc obj {0} runs no random shop (merchant_random_pack_id 0)", Bc);
            return;
        }

        var pack = RandomMerchantManager.Instance.TryGetPack(packId);
        try
        {
            var result = RandomMerchantManager.Instance.TryRefresh(
                character.Id,
                packId,
                RefreshFree,
                DateTime.UtcNow,
                RefreshFree ? null : () => pack != null && ChargeRefresh(character, pack));

            if (result == RandomShopRefreshResult.Refreshed)
            {
                Connection.SendPacket(new SCRandomShopInfoRefreshPacket(0));
                return;
            }

            // Failure codes for this family are not decoded in the corpus: say it in the log and
            // send nothing rather than invent an ErrorMessage value.
            Logger.Warn("Random shop refresh refused for character {0}, pack {1}: {2}", character.Id, packId, result);
        }
        catch (RandomMerchantContentException ex)
        {
            Logger.Error(ex, "Random shop refresh refused for character {0}, pack {1}", character.Id, packId);
        }
    }

    /// <summary>
    /// Takes the paid refresh charge in the pack's own content currency: refresh_point of
    /// refresh_currency_id, consumed as refresh_item_id when that currency is item points.
    /// </summary>
    private static bool ChargeRefresh(Character character, RandomMerchantPack pack)
    {
        if (pack.RefreshPoint <= 0)
            return true; // content prices it at zero - nothing is charged

        switch (pack.RefreshCurrency)
        {
            case ContentCurrencyType.ItemPoint:
                return character.Inventory.Bag.ConsumeItem(
                           ItemTaskType.StoreBuy, pack.RefreshItemId, pack.RefreshPoint, null) == pack.RefreshPoint;
            case ContentCurrencyType.LivingPoint:
                if (character.VocationPoint < pack.RefreshPoint)
                    return false;
                character.ChangeGamePoints(GamePointKind.Vocation, -pack.RefreshPoint);
                return true;
            case ContentCurrencyType.Gold:
                return character.SubtractMoney(SlotType.Inventory, pack.RefreshPoint, ItemTaskType.StoreBuy);
            default:
                Logger.Error(
                    "Random shop: refresh_currency_id {0} of pack {1} has no charge path - refusing the paid refresh",
                    pack.RefreshCurrencyId, pack.Id);
                return false;
        }
    }
}
