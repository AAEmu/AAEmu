using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// u8 buy count (maximum 12), u8 buyback count (maximum 16), buy entries
/// (u32 item type, u8 grade, i32 stack, u8 currency), i32 buyback slots, bool use AA point,
/// and u8 open type.
/// </summary>
public class CSBuyItemsPacket() : GamePacket(CSOffsets.CSBuyItemsPacket, 1)
{
    private const byte MaxBuyEntries = 12;
    private const byte MaxBuybackEntries = 16;
    private const float MerchantInteractionRange = 3f;

    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        var npcObjId = stream.ReadBc();
        var doodadObjId = stream.ReadBc();
        var shopType = stream.ReadUInt32();
        var buyCount = stream.ReadByte();
        var buybackCount = stream.ReadByte();

        if (buyCount > MaxBuyEntries || buybackCount > MaxBuybackEntries)
            return;

        var requestedGoods = new List<RequestedGood>(buyCount);
        for (var index = 0; index < buyCount; index++)
        {
            requestedGoods.Add(new RequestedGood(
                stream.ReadUInt32(),
                stream.ReadByte(),
                stream.ReadInt32(),
                (ShopCurrencyType)stream.ReadByte()));
        }

        var buybackSlots = new List<int>(buybackCount);
        for (var index = 0; index < buybackCount; index++)
            buybackSlots.Add(stream.ReadInt32());

        var useAaPoint = stream.ReadBoolean();
        var openType = stream.ReadByte();

        Logger.Debug(
            "BuyItems npc={0}, doodad={1}, shopType={2}, buys={3}, buybacks={4}, useAAPoint={5}, openType={6}",
            npcObjId, doodadObjId, shopType, buyCount, buybackCount, useAaPoint, openType);

        var pack = ResolveMerchantPack(character, npcObjId, doodadObjId);
        if (pack == null || shopType != (uint)pack.Kind)
            return;

        var purchases = new List<Purchase>(requestedGoods.Count);
        var costs = new Dictionary<ShopCurrencyType, long>();
        foreach (var request in requestedGoods)
        {
            if (request.Count <= 0)
                return;

            var good = pack.GetItem(request.ItemId, request.Grade);
            if (good == null || good.Currency != request.Currency ||
                ItemManager.Instance.GetTemplate(request.ItemId) == null)
                return;

            if (!TryAddCost(costs, good.Currency, good.Cost, request.Count))
                return;

            purchases.Add(new Purchase(good, request.Count));
        }

        var buybacks = new List<(Item Item, int Slot)>();
        var usedBuybackItems = new HashSet<ulong>();
        foreach (var slot in buybackSlots)
        {
            var item = character.BuyBackItems.GetItemBySlot(slot);
            if (item == null || !usedBuybackItems.Add(item.Id))
                return;

            var grade = ItemManager.Instance.GetGradeTemplate(item.Grade);
            if (grade == null)
                return;

            var unitPrice = (long)Math.Floor(item.Template.Refund * grade.RefundMultiplier / 100f);
            if (unitPrice < 0 || !TryAddCost(costs, ShopCurrencyType.Money, unitPrice, item.Count))
                return;

            buybacks.Add((item, slot));
        }

        if (!HasBagSpace(character, purchases, buybacks.Select(entry => entry.Item)))
        {
            character.SendErrorMessage(ErrorMessageType.BagFull);
            return;
        }

        if (!CanPay(character, pack, costs, useAaPoint))
            return;

        if (!NpcManager.Instance.TryReserveMerchantPurchases(
                character.Id,
                purchases.Select(purchase => (purchase.Good, purchase.Count)),
                out var failedGood,
                out var updatedPurchaseStates))
        {
            if (failedGood != null)
            {
                Connection.SendPacket(new SCBuyFailedMerchantGoodLimitPurchasePacket(
                    failedGood.ItemTemplateId,
                    failedGood.PurchaseType,
                    failedGood.PurchaseLimit));
            }
            return;
        }

        if (!Pay(character, pack, costs, useAaPoint))
        {
            if (!NpcManager.Instance.TryRollbackMerchantPurchases(
                    character.Id,
                    purchases.Select(purchase => (purchase.Good, purchase.Count))))
            {
                Logger.Error("Failed to roll back merchant purchase limits for character {0}", character.Id);
            }
            return;
        }

        foreach (var purchase in purchases)
        {
            if (!character.Inventory.Bag.AcquireDefaultItem(
                    ItemTaskType.StoreBuy,
                    purchase.Good.ItemTemplateId,
                    purchase.Count,
                    purchase.Good.Grade))
            {
                Logger.Error(
                    "Authoritative merchant acquisition failed after preflight for character {0}, item {1}, count {2}",
                    character.Id, purchase.Good.ItemTemplateId, purchase.Count);
                return;
            }
        }

        var buybackTasks = new List<ItemTask>();
        foreach (var (item, _) in buybacks)
        {
            if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, item))
            {
                Logger.Error(
                    "Authoritative buyback move failed after preflight for character {0}, item {1}",
                    character.Id, item.Id);
                return;
            }

            buybackTasks.Add(new ItemBuyback(item));
        }

        if (buybackTasks.Count > 0 || purchases.Count == 0)
            Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy, buybackTasks, []));

        if (updatedPurchaseStates.Count > 0)
            Connection.SendPacket(new SCUpdateMerchantGoodLimitPurchasePacket(updatedPurchaseStates));
    }

    private static MerchantGoods ResolveMerchantPack(Character character, uint npcObjId, uint doodadObjId)
    {
        if ((npcObjId == 0) == (doodadObjId == 0))
            return null;

        if (npcObjId != 0)
        {
            var npc = character.ParentWorld.GetNpc(npcObjId);
            if (npc == null || !npc.Template.Merchant || npc.Template.MerchantPackId == 0)
                return null;

            if (MathUtil.CalculateDistance(character.Transform.World.Position, npc.Transform.World.Position) > MerchantInteractionRange)
            {
                character.SendErrorMessage(ErrorMessageType.TooFarAway);
                return null;
            }

            return NpcManager.Instance.GetGoods(npc.Template.MerchantPackId);
        }

        var doodad = character.ParentWorld.GetDoodad(doodadObjId);
        if (doodad == null)
            return null;
        if (MathUtil.CalculateDistance(character.Transform.World.Position, doodad.Transform.World.Position) > MerchantInteractionRange)
        {
            character.SendErrorMessage(ErrorMessageType.TooFarAway);
            return null;
        }

        var packId = DoodadManager.Instance.GetStoreMerchantPackId(doodad);
        return packId == 0 ? null : NpcManager.Instance.GetGoods(packId);
    }

    private static bool TryAddCost(
        Dictionary<ShopCurrencyType, long> costs,
        ShopCurrencyType currency,
        long unitCost,
        int count)
    {
        if (unitCost < 0 || count < 0 || unitCost > long.MaxValue / Math.Max(count, 1))
            return false;

        var amount = unitCost * count;
        costs.TryGetValue(currency, out var current);
        if (amount > long.MaxValue - current)
            return false;

        costs[currency] = current + amount;
        return true;
    }

    private static bool HasBagSpace(
        Character character,
        IEnumerable<Purchase> purchases,
        IEnumerable<Item> buybacks)
    {
        var incoming = purchases
            .Select(purchase => (
                ItemTemplateId: purchase.Good.ItemTemplateId,
                Grade: purchase.Good.Grade,
                Count: purchase.Count))
            .Concat(buybacks.Select(item => (
                ItemTemplateId: item.TemplateId,
                Grade: item.Grade,
                Count: item.Count)))
            .GroupBy(entry => (entry.ItemTemplateId, entry.Grade))
            .Select(group => (group.Key.ItemTemplateId, group.Key.Grade, Count: group.Sum(entry => (long)entry.Count)));

        var freeSlots = character.Inventory.Bag.FreeSlotCount;
        foreach (var entry in incoming)
        {
            var template = ItemManager.Instance.GetTemplate(entry.ItemTemplateId);
            if (template == null || template.MaxCount <= 0)
                return false;

            character.Inventory.Bag.GetAllItemsByTemplate(
                entry.ItemTemplateId,
                entry.Grade,
                out var currentItems,
                out var currentCount);
            var existingSpace = (long)currentItems.Count * template.MaxCount - currentCount;
            var remaining = Math.Max(0, entry.Count - existingSpace);
            var requiredSlots = (remaining + template.MaxCount - 1) / template.MaxCount;
            if (requiredSlots > freeSlots)
                return false;

            freeSlots -= (int)requiredSlots;
        }

        return true;
    }

    private static bool CanPay(
        Character character,
        MerchantGoods pack,
        IReadOnlyDictionary<ShopCurrencyType, long> costs,
        bool useAaPoint)
    {
        if (!CostsFitInt(costs))
            return false;

        var moneyBalance = useAaPoint ? character.AaPoint : character.Money;
        if (costs.GetValueOrDefault(ShopCurrencyType.Money) > moneyBalance)
        {
            character.SendErrorMessage(useAaPoint
                ? ErrorMessageType.NotEnoughAaPoint
                : ErrorMessageType.NotEnoughMoney);
            return false;
        }

        if (costs.GetValueOrDefault(ShopCurrencyType.Honor) > character.HonorPoint)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughHonorPoint);
            return false;
        }

        if (costs.GetValueOrDefault(ShopCurrencyType.VocationBadges) > character.VocationPoint)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughLivingPoint);
            return false;
        }

        var itemPointCost = (int)costs.GetValueOrDefault(ShopCurrencyType.ItemPoint);
        if (itemPointCost <= 0)
            return true;

        if (pack.Kind == MerchantPackKind.ItemPoint)
        {
            var contributionPoint = character.Expedition?.GetMember(character)?.ContributionPoint ?? 0;
            if (contributionPoint >= itemPointCost)
                return true;

            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return false;
        }

        if (pack.ItemPointId == 0)
            return false;

        character.Inventory.Bag.GetAllItemsByTemplate(pack.ItemPointId, -1, out _, out var itemPointCount);
        if (itemPointCount >= itemPointCost)
            return true;

        character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
        return false;
    }

    private static bool CostsFitInt(IReadOnlyDictionary<ShopCurrencyType, long> costs)
    {
        return costs.Values.All(cost => cost is >= 0 and <= int.MaxValue);
    }

    private static bool Pay(
        Character character,
        MerchantGoods pack,
        IReadOnlyDictionary<ShopCurrencyType, long> costs,
        bool useAaPoint)
    {
        var itemPoint = (int)costs.GetValueOrDefault(ShopCurrencyType.ItemPoint);
        if (itemPoint > 0)
        {
            var itemPointPaid = pack.Kind == MerchantPackKind.ItemPoint
                ? ExpeditionManager.Instance.TryChangeContributionPoints(character, -itemPoint, false)
                : character.Inventory.Bag.ConsumeItem(
                    ItemTaskType.StoreBuy,
                    pack.ItemPointId,
                    itemPoint,
                    null) == itemPoint;
            if (!itemPointPaid)
                return false;
        }

        var money = (int)costs.GetValueOrDefault(ShopCurrencyType.Money);
        var moneyPaid = money <= 0 || (useAaPoint
            ? character.SubtractAAPoint(SlotType.Inventory, money, ItemTaskType.StoreBuy)
            : character.SubtractMoney(SlotType.Inventory, money, ItemTaskType.StoreBuy));
        if (!moneyPaid)
        {
            RefundItemPoint(character, pack, itemPoint);
            return false;
        }

        var honor = (int)costs.GetValueOrDefault(ShopCurrencyType.Honor);
        if (honor > 0)
            character.ChangeGamePoints(GamePointKind.Honor, -honor);

        var vocation = (int)costs.GetValueOrDefault(ShopCurrencyType.VocationBadges);
        if (vocation > 0)
            character.ChangeGamePoints(GamePointKind.Vocation, -vocation);

        return true;
    }

    private static void RefundItemPoint(Character character, MerchantGoods pack, int itemPoint)
    {
        if (itemPoint <= 0)
            return;

        if (pack.Kind == MerchantPackKind.ItemPoint)
        {
            if (!ExpeditionManager.Instance.TryChangeContributionPoints(character, itemPoint, false))
                Logger.Error("Failed to refund {0} contribution points to character {1}", itemPoint, character.Id);
            return;
        }

        if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.StoreBuy, pack.ItemPointId, itemPoint, -1))
            Logger.Error("Failed to refund {0} item points of template {1} to character {2}", itemPoint, pack.ItemPointId, character.Id);
    }

    private sealed record RequestedGood(uint ItemId, byte Grade, int Count, ShopCurrencyType Currency);
    private sealed record Purchase(MerchantGoodsItem Good, int Count);
}
