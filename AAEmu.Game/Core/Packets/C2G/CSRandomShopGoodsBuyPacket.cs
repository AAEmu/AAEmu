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
/// Buys offers from the viewer's random shop window: one requested offer per list element,
/// claimed exactly once, then acknowledged with <see cref="G2C.SCRandomShopGoodsBuyPacket"/>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value:
/// u8 shopType, bc npc (3 bytes), bc doodad (3 bytes), u32 type, bool useAApoint, then the
/// requested-offer list "buyGoods" - a block whose begin/end tags carry no wire bytes, framed as
/// u32 Size followed by one u32 "type" per requested offer. Each element is the display map key
/// of the offer the client wants, i.e. its slot. useAApoint is parsed but not acted on: which
/// currency an offer bills is decided by its content pack, not by the request.
/// </remarks>
public class CSRandomShopGoodsBuyPacket() : GamePacket(CSOffsets.CSRandomShopGoodsBuyPacket, 1)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public byte ShopType { get; private set; }
    public uint NpcObjId { get; private set; }
    public uint DoodadObjId { get; private set; }
    public uint Type { get; private set; }
    public bool UseAaPoint { get; private set; }

    /// <summary>Display map keys (offer slots) the client asked to buy, in wire order.</summary>
    public List<uint> RequestedGoods { get; } = [];

    public override void Read(PacketStream stream)
    {
        ShopType = stream.ReadByte();
        NpcObjId = stream.ReadBc();
        DoodadObjId = stream.ReadBc();
        Type = stream.ReadUInt32();
        UseAaPoint = stream.ReadBoolean();

        // buyGoods block: u32 Size, then one u32 element per requested offer.
        var size = stream.ReadUInt32();
        for (var i = 0u; i < size; i++)
            RequestedGoods.Add(stream.ReadUInt32());

        HandleBuy();
    }

    private void HandleBuy()
    {
        if (Connection?.ActiveChar is not { } character)
            return;

        var packId = character.ParentWorld?.GetNpc(NpcObjId)?.Template?.MerchantRandomPackId ?? 0;
        if (packId == 0)
        {
            Logger.Warn("Random shop buy: npc obj {0} runs no random shop (merchant_random_pack_id 0)", NpcObjId);
            return;
        }

        var now = DateTime.UtcNow;
        var bought = new List<uint>(RequestedGoods.Count);
        foreach (var key in RequestedGoods)
        {
            var offer = RandomMerchantManager.Instance
                .GetWindow(character.Id, packId, now)
                .Offers.FirstOrDefault(candidate => candidate.Slot == (int)key);

            var pack = RandomMerchantManager.Instance.FindPack(packId);
            var result = RandomMerchantManager.Instance.TryPurchase(
                character.Id, packId, (int)key, now,
                offer == null || pack == null ? null : () => ChargeAndGrant(character, pack, offer));

            switch (result)
            {
                case RandomShopPurchaseResult.Purchased:
                    bought.Add(key);
                    break;
                case RandomShopPurchaseResult.PaymentFailed:
                    Logger.Warn(
                        "Random shop buy refused for character {0}, pack {1}, slot {2}: the charge was refused",
                        character.Id, packId, key);
                    break;
                default:
                    Logger.Warn(
                        "Random shop buy refused for character {0}, pack {1}, slot {2}: {3}",
                        character.Id, packId, key, result);
                    break;
            }
        }

        if (bought.Count == 0)
        {
            // Failure codes for this family are not decoded in the corpus: say it in the log and
            // send nothing rather than invent an ErrorMessage value.
            Logger.Warn(
                "Random shop buy: character {0}, pack {1} bought nothing of {2} requested offer(s)",
                character.Id, packId, RequestedGoods.Count);
            return;
        }

        Connection.SendPacket(new SCRandomShopGoodsBuyPacket(0, Type, bought));
    }

    /// <summary>
    /// Takes one offer's price, then puts the item in the bag. A failed grant refunds the price
    /// and the purchase is released.
    /// </summary>
    private static bool ChargeAndGrant(Character character, RandomMerchantPack pack, RandomShopOffer offer)
    {
        if (character.Inventory.Bag.SpaceLeftForItem(offer.ItemId) < 1)
        {
            Logger.Warn("Random shop: character {0} has no bag space for item {1}", character.Id, offer.ItemId);
            return false;
        }

        if (offer.Cost > 0 && !ChargeOffer(character, pack, offer))
            return false;

        var granted = character.Inventory.Bag.AcquireDefaultItemEx(
            ItemTaskType.StoreBuy, offer.ItemId, 1, offer.Grade, out _, out _, character.Id);
        if (granted)
            return true;

        if (offer.Cost > 0)
            RefundOffer(character, pack, offer);
        Logger.Error("Random shop: grant of item {0} failed for character {1}; the price was refunded",
            offer.ItemId, character.Id);
        return false;
    }

    private static bool ChargeOffer(Character character, RandomMerchantPack pack, RandomShopOffer offer)
    {
        switch (offer.Currency)
        {
            case ShopCurrencyType.Money:
                return character.SubtractMoney(SlotType.Inventory, offer.Cost, ItemTaskType.StoreBuy);
            case ShopCurrencyType.Honor:
                if (character.HonorPoint < offer.Cost)
                    return false;
                character.ChangeGamePoints(GamePointKind.Honor, -offer.Cost);
                return true;
            case ShopCurrencyType.VocationBadges:
                if (character.VocationPoint < offer.Cost)
                    return false;
                character.ChangeGamePoints(GamePointKind.Vocation, -offer.Cost);
                return true;
            case ShopCurrencyType.ItemPoint:
                return ChargeItemPoint(character, pack, offer.Cost);
            default:
                Logger.Error(
                    "Random shop: offer slot {0} bills currency {1}, which has no charge path - refusing the purchase",
                    offer.Slot, offer.Currency);
                return false;
        }
    }

    private static bool ChargeItemPoint(Character character, RandomMerchantPack pack, int cost)
    {
        if (pack.ItemPointId == 0)
        {
            Logger.Error("Random shop: pack {0} bills item points but item_point_id is 0 - refusing", pack.Id);
            return false;
        }

        character.Inventory.Bag.GetAllItemsByTemplate(pack.ItemPointId, -1, out _, out var held);
        if (held < cost)
            return false;
        return character.Inventory.Bag.ConsumeItem(ItemTaskType.StoreBuy, pack.ItemPointId, cost, null) == cost;
    }

    private static void RefundOffer(Character character, RandomMerchantPack pack, RandomShopOffer offer)
    {
        switch (offer.Currency)
        {
            case ShopCurrencyType.Money:
                character.AddMoney(SlotType.Inventory, offer.Cost, ItemTaskType.StoreBuy);
                break;
            case ShopCurrencyType.Honor:
                character.ChangeGamePoints(GamePointKind.Honor, offer.Cost);
                break;
            case ShopCurrencyType.VocationBadges:
                character.ChangeGamePoints(GamePointKind.Vocation, offer.Cost);
                break;
            case ShopCurrencyType.ItemPoint:
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, pack.ItemPointId, offer.Cost, -1);
                break;
        }
    }
}
