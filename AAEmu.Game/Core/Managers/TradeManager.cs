using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class TradeTemplate
{
    public uint Id { get; set; }
    public uint OwnerObjId { get; set; }
    public uint TargetObjId { get; set; }
    public bool LockOwner { get; set; }
    public bool LockTarget { get; set; }
    public bool OkOwner { get; set; }
    public bool OkTarget { get; set; }
    public List<TradeItemEntry> OwnerItems { get; set; }
    public List<TradeItemEntry> TargetItems { get; set; }
    public long OwnerMoneyPutup { get; set; }
    public long TargetMoneyPutup { get; set; }
}

public sealed record TradeItemEntry(ulong ItemId, SlotType SlotType, byte Slot, int Amount);

internal sealed record ResolvedTradeItem(TradeItemEntry Entry, Item Item);

internal sealed class StagedTradeTransfer
{
    public required Character Source { get; init; }
    public required Character Target { get; init; }
    public required Item SourceItem { get; init; }
    public required Item TransferredItem { get; init; }
    public required ItemTask SourceTask { get; init; }
    public required byte SourceSlot { get; init; }
    public required int Amount { get; init; }
    public required bool WasSplit { get; init; }
    public bool AddedToTarget { get; set; }
}

public class TradeManager(ITradeIdManager tradeIdManager, IWorldManager worldManager) : Singleton<TradeManager>, ITradeManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<uint, TradeTemplate> _trades = [];
    private readonly Dictionary<uint, uint> _pendingInvites = [];
    private readonly Lock _pendingInvitesLock = new();

    private uint GetTradeId(uint objId)
    {
        if (_trades.Count > 0)
        {
            foreach (var (key, value) in _trades)
            {
                if (value.OwnerObjId.Equals(objId)) return key;
                if (value.TargetObjId.Equals(objId)) return key;
            }
        }

        return 0;
    }

    private bool IsTrading(uint objId)
    {
        return GetTradeId(objId) != 0;
    }

    private static bool MeetsTradeLevel(Character character)
    {
        return character.Level + character.HeirLevel >= AppConfiguration.Instance.LevelRestrictions.TradeLevel;
    }

    private void UnlockTrade(Character owner, Character target, uint tradeId)
    {
        if (!_trades[tradeId].LockOwner && !_trades[tradeId].LockTarget) return;

        _trades[tradeId].LockOwner = false;
        _trades[tradeId].LockTarget = false;
        _trades[tradeId].OkOwner = false;
        _trades[tradeId].OkTarget = false;
        owner.SendPacket(new SCTradeLockUpdatePacket(false, false));
        target.SendPacket(new SCTradeLockUpdatePacket(false, false));
        Logger.Info("Trade Id:{0} Lockers opened and Ok undone.", tradeId);
    }

    public void CanStartTrade(Character owner, Character target)
    {
        if (!AppConfiguration.Instance.InitialConfig.CanTrade)
            return;

        if (!MeetsTradeLevel(owner) || !MeetsTradeLevel(target))
        {
            owner.SendPacket(new SCCannotStartTradePacket(
                target.ObjId, (int)ErrorMessageType.TradeTargetIsNotPossibleState));
            return;
        }

        if (owner.IsInBattle || target.IsInBattle)
        {
            owner.SendPacket(new SCCannotStartTradePacket(
                target.ObjId, (int)ErrorMessageType.TradeIsNotPossibleInCombat));
            return;
        }

        if (owner == target || owner.IsDead || target.IsDead || !owner.IsOnline || !target.IsOnline ||
            owner.Transform.InstanceId != target.Transform.InstanceId ||
            owner.GetRelationStateTo(target) == RelationState.Hostile ||
            IsTrading(owner.ObjId) || IsTrading(target.ObjId))
        {
            owner.SendPacket(new SCCannotStartTradePacket(
                target.ObjId, (int)ErrorMessageType.TradeTargetIsNotPossibleState));
            return;
        }

        lock (_pendingInvitesLock)
            _pendingInvites[target.ObjId] = owner.ObjId;

        Logger.Info("{0}({1}) is trying to trade with {2}({3}).", owner.Name, owner.ObjId, target.Name, target.ObjId);
        target.SendPacket(new SCCanStartTradePacket(owner.ObjId));
    }

    public void StartTrade(Character owner, Character target)
    {
        lock (_pendingInvitesLock)
        {
            if (!_pendingInvites.Remove(target.ObjId, out var invitingObjId) || invitingObjId != owner.ObjId)
            {
                Logger.Warn("Rejected unsolicited trade acceptance from {0}({1}) for {2}({3}).",
                    target.Name, target.ObjId, owner.Name, owner.ObjId);
                return;
            }
        }

        if (owner.IsInBattle || target.IsInBattle)
        {
            owner.SendPacket(new SCCannotStartTradePacket(
                target.ObjId, (int)ErrorMessageType.TradeIsNotPossibleInCombat));
            return;
        }

        if (!AppConfiguration.Instance.InitialConfig.CanTrade || !MeetsTradeLevel(owner) || !MeetsTradeLevel(target) ||
            owner.IsDead || target.IsDead ||
            !owner.IsOnline || !target.IsOnline ||
            owner.Transform.InstanceId != target.Transform.InstanceId ||
            owner.GetRelationStateTo(target) == RelationState.Hostile ||
            IsTrading(owner.ObjId) || IsTrading(target.ObjId))
        {
            owner.SendPacket(new SCCannotStartTradePacket(
                target.ObjId, (int)ErrorMessageType.TradeTargetIsNotPossibleState));
            return;
        }

        var nextId = tradeIdManager.GetNextId();
        var template = new TradeTemplate
        {
            Id = nextId,
            OwnerObjId = owner.ObjId,
            TargetObjId = target.ObjId,
            LockOwner = false,
            LockTarget = false,
            OkOwner = false,
            OkTarget = false,
            OwnerItems = [],
            TargetItems = [],
            OwnerMoneyPutup = 0,
            TargetMoneyPutup = 0

        };
        _trades.Add(nextId, template);

        Logger.Info("Trade Id:{4} started between {0}({1}) - {2}({3}).", owner.Name, owner.ObjId, target.Name, target.ObjId, nextId);
        owner.SendPacket(new SCTradeStartedPacket(target.ObjId));
        target.SendPacket(new SCTradeStartedPacket(owner.ObjId));
    }

    public void CannotStartTrade(Character owner, Character target, int reason)
    {
        lock (_pendingInvitesLock)
        {
            if (!_pendingInvites.Remove(target.ObjId, out var invitingObjId) || invitingObjId != owner.ObjId)
            {
                Logger.Warn("Rejected unsolicited trade refusal from {0}({1}) for {2}({3}).",
                    target.Name, target.ObjId, owner.Name, owner.ObjId);
                return;
            }
        }

        owner.SendPacket(new SCCannotStartTradePacket(target.ObjId, reason));
    }

    public void CancelTrade(uint objId, int reason, uint tradeId = 0u)
    {
        tradeId = tradeId == 0 ? GetTradeId(objId) : tradeId;
        if (tradeId == 0)
        {
            worldManager.GetCharacterByObjId(objId)?.SendPacket(new SCTradeCanceledPacket(reason, true));
            return;
        }

        if (!_trades.Remove(tradeId, out var trade))
            return;

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);

        Logger.Info("Trade Id:{0} between owner obj={1} and target obj={2} is canceled.",
            tradeId, trade.OwnerObjId, trade.TargetObjId);
        var causedByOwner = trade.OwnerObjId == objId;
        owner?.SendPacket(new SCTradeCanceledPacket(reason, causedByOwner));
        target?.SendPacket(new SCTradeCanceledPacket(reason, !causedByOwner));
    }

    public void AddItem(Character character, SlotType slotType, byte slot, int amount)
    {
        var tradeId = GetTradeId(character.ObjId);
        if (tradeId == 0)
            return;

        if (slotType != SlotType.Inventory || amount <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.TradeInvalidItem);
            return;
        }

        var item = character.Inventory.GetItem(slotType, slot);
        if (item == null || item.OwnerId != character.Id || item.Count < amount)
        {
            character.SendErrorMessage(ErrorMessageType.TradeInvalidItem);
            return;
        }

        if (item.HasFlag(ItemFlag.SoulBound))
        {
            character.SendErrorMessage(ErrorMessageType.TradeSoulBoundItem);
            return;
        }

        var trade = _trades[tradeId];
        var isOwnerWhoAdd = trade.OwnerObjId == character.ObjId;
        var offeredItems = isOwnerWhoAdd ? trade.OwnerItems : trade.TargetItems;
        if (offeredItems.Any(entry => entry.ItemId == item.Id))
            return;

        var currentMoneyDelta = trade.TargetMoneyPutup - trade.OwnerMoneyPutup;
        if (trade.OwnerItems.Count + trade.TargetItems.Count + 1 + (currentMoneyDelta == 0 ? 0 : 1) >
            ItemTaskListLimits.Tasks)
            return;

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);
        if (owner == null || target == null)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        offeredItems.Add(new TradeItemEntry(item.Id, slotType, slot, amount));
        Logger.Info("Trade Id:{0} {1}({2}) added item ({3}-{4}) Amount: {5}.",
            tradeId, character.Name, character.ObjId, slotType, slot, amount);

        character.SendPacket(new SCTradeItemPutupPacket(slotType, slot, amount));
        (isOwnerWhoAdd ? target : owner).SendPacket(new SCOtherTradeItemPutupPacket(item, amount));
        UnlockTrade(owner, target, tradeId);
    }

    public void AddMoney(Character character, long moneyAmount)
    {
        var tradeId = GetTradeId(character.ObjId);
        if (tradeId == 0)
            return;

        if (moneyAmount < 0 || character.Money < moneyAmount)
        {
            character.SendErrorMessage(ErrorMessageType.TradeNotEnoughMoney);
            return;
        }

        var trade = _trades[tradeId];
        var isOwnerWhoAdd = trade.OwnerObjId == character.ObjId;
        var ownerMoney = isOwnerWhoAdd ? moneyAmount : trade.OwnerMoneyPutup;
        var targetMoney = isOwnerWhoAdd ? trade.TargetMoneyPutup : moneyAmount;
        if (trade.OwnerItems.Count + trade.TargetItems.Count + (targetMoney == ownerMoney ? 0 : 1) >
            ItemTaskListLimits.Tasks)
            return;

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);
        if (owner == null || target == null)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        Logger.Info("Trade Id:{0} {1}({2}) changed Money: {3}.", tradeId, character.Name, character.ObjId, moneyAmount);
        if (isOwnerWhoAdd)
            trade.OwnerMoneyPutup = moneyAmount;
        else
            trade.TargetMoneyPutup = moneyAmount;

        character.SendPacket(new SCTradeMoneyPutupPacket(moneyAmount));
        (isOwnerWhoAdd ? target : owner).SendPacket(new SCOtherTradeMoneyPutupPacket(moneyAmount));
        UnlockTrade(owner, target, tradeId);
    }

    public void RemoveItem(Character character, SlotType slotType, byte slot)
    {
        var tradeId = GetTradeId(character.ObjId);
        if (tradeId == 0)
            return;

        var trade = _trades[tradeId];
        var isOwnerWhoAdd = trade.OwnerObjId == character.ObjId;
        var offeredItems = isOwnerWhoAdd ? trade.OwnerItems : trade.TargetItems;
        var entry = offeredItems.FirstOrDefault(x => x.SlotType == slotType && x.Slot == slot);
        if (entry == null)
            return;

        var item = character.Inventory.GetItem(slotType, slot);
        if (item == null || item.Id != entry.ItemId)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);
        if (owner == null || target == null)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        offeredItems.Remove(entry);
        Logger.Info("Trade Id:{0} {1}({2}) took down item ({3}-{4}).",
            tradeId, character.Name, character.ObjId, slotType, slot);
        character.SendPacket(new SCTradeItemTookdownPacket(slotType, slot));
        (isOwnerWhoAdd ? target : owner).SendPacket(new SCOtherTradeItemTookdownPacket(item, entry.Amount));
        UnlockTrade(owner, target, tradeId);
    }

    public void LockTrade(Character character, bool isLocked)
    {
        var tradeId = GetTradeId(character.ObjId);
        if (tradeId == 0)
            return;

        var trade = _trades[tradeId];
        var isOwner = trade.OwnerObjId == character.ObjId;
        if ((isOwner && trade.LockOwner == isLocked) || (!isOwner && trade.LockTarget == isLocked))
            return;

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);
        if (owner == null || target == null)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        if (!isLocked)
        {
            trade.LockOwner = false;
            trade.LockTarget = false;
            trade.OkOwner = false;
            trade.OkTarget = false;
            Logger.Info("Trade Id:{0} {1}({2}) unlocked the offer.", tradeId, character.Name, character.ObjId);
        }
        else if (isOwner)
            trade.LockOwner = true;
        else
            trade.LockTarget = true;

        owner.SendPacket(new SCTradeLockUpdatePacket(trade.LockOwner, trade.LockTarget));
        target.SendPacket(new SCTradeLockUpdatePacket(trade.LockTarget, trade.LockOwner));
    }

    public void OkTrade(Character character)
    {
        var tradeId = GetTradeId(character.ObjId);
        if (tradeId == 0)
            return;

        var trade = _trades[tradeId];
        if (!trade.LockOwner || !trade.LockTarget)
            return;

        var owner = worldManager.GetCharacterByObjId(trade.OwnerObjId);
        var target = worldManager.GetCharacterByObjId(trade.TargetObjId);
        if (owner == null || target == null)
        {
            CancelTrade(character.ObjId, 0, tradeId);
            return;
        }

        if (trade.OwnerObjId == character.ObjId)
            trade.OkOwner = true;
        else if (trade.TargetObjId == character.ObjId)
            trade.OkTarget = true;
        else
            return;

        Logger.Info("Trade Id:{0} {1}({2}) accepted the locked offer.", tradeId, character.Name, character.ObjId);
        owner.SendPacket(new SCTradeOkUpdatePacket(trade.OkOwner, trade.OkTarget));
        target.SendPacket(new SCTradeOkUpdatePacket(trade.OkTarget, trade.OkOwner));

        if (trade.OkOwner && trade.OkTarget)
            FinishTrade(owner, target, tradeId);
    }

    private static bool TryResolveItems(
        Character character,
        List<TradeItemEntry> entries,
        out List<ResolvedTradeItem> resolved)
    {
        resolved = new List<ResolvedTradeItem>(entries.Count);
        foreach (var entry in entries)
        {
            var item = character.Inventory.GetItem(entry.SlotType, entry.Slot);
            if (item == null || item.Id != entry.ItemId || item.OwnerId != character.Id ||
                item._holdingContainer != character.Inventory.Bag || entry.Amount <= 0 ||
                entry.Amount > item.Count || item.HasFlag(ItemFlag.SoulBound))
                return false;

            resolved.Add(new ResolvedTradeItem(entry, item));
        }

        return true;
    }

    private static Item CreateSplitItem(Item source, int amount)
    {
        var split = ItemManager.Instance.Create(source.TemplateId, amount, source.Grade);
        if (split == null)
            return null;

        split.ItemFlags = source.ItemFlags;
        split.LifespanMins = source.LifespanMins;
        split.MadeUnitId = source.MadeUnitId;
        split.CreateTime = source.CreateTime;
        split.UnsecureTime = source.UnsecureTime;
        split.UnpackTime = source.UnpackTime;
        split.ImageItemTemplateId = source.ImageItemTemplateId;
        split.UccId = source.UccId;
        split.ExpirationTime = source.ExpirationTime;
        split.ExpirationOnlineMinutesLeft = source.ExpirationOnlineMinutesLeft;
        split.ChargeStartTime = source.ChargeStartTime;
        split.ChargeCount = source.ChargeCount;
        split.ChargeUseSkillTime = source.ChargeUseSkillTime;
        split.DetailType = source.DetailType;
        split.Detail = source.Detail?.ToArray();
        return split;
    }

    private static bool StageItems(
        Character source,
        Character target,
        List<ResolvedTradeItem> items,
        List<StagedTradeTransfer> staged)
    {
        foreach (var resolved in items)
        {
            var item = resolved.Item;
            var entry = resolved.Entry;
            var sourceSlot = (byte)item.Slot;

            if (entry.Amount == item.Count)
            {
                var sourceTask = new ItemRemove(item);
                if (!source.Inventory.Bag.Items.Remove(item))
                    return false;

                source.Inventory.Bag.UpdateFreeSlotCount();
                staged.Add(new StagedTradeTransfer
                {
                    Source = source,
                    Target = target,
                    SourceItem = item,
                    TransferredItem = item,
                    SourceTask = sourceTask,
                    SourceSlot = sourceSlot,
                    Amount = entry.Amount,
                    WasSplit = false
                });
                continue;
            }

            if (item.Template.MaxCount <= 1 || item.GetType() != typeof(Item))
                return false;

            var split = CreateSplitItem(item, entry.Amount);
            if (split == null)
                return false;

            item.Count -= entry.Amount;
            staged.Add(new StagedTradeTransfer
            {
                Source = source,
                Target = target,
                SourceItem = item,
                TransferredItem = split,
                SourceTask = new ItemCountUpdate(item, -entry.Amount),
                SourceSlot = sourceSlot,
                Amount = entry.Amount,
                WasSplit = true
            });
        }

        return true;
    }

    private static bool CommitStagedItems(
        IReadOnlyCollection<StagedTradeTransfer> staged,
        ICollection<ItemTask> ownerTasks,
        ICollection<ItemTask> targetTasks,
        Character owner)
    {
        foreach (var transfer in staged)
        {
            if (!transfer.Target.Inventory.Bag.AddOrMoveExistingItem(
                    ItemTaskType.Invalid, transfer.TransferredItem))
                return false;

            transfer.AddedToTarget = true;
            var sourceTasks = transfer.Source == owner ? ownerTasks : targetTasks;
            var recipientTasks = transfer.Target == owner ? ownerTasks : targetTasks;
            sourceTasks.Add(transfer.SourceTask);
            recipientTasks.Add(new ItemAdd(transfer.TransferredItem));
        }

        return true;
    }

    private static void RollbackTransfers(List<StagedTradeTransfer> staged)
    {
        for (var index = staged.Count - 1; index >= 0; index--)
        {
            var transfer = staged[index];
            if (transfer.WasSplit)
            {
                if (transfer.AddedToTarget)
                    transfer.Target.Inventory.Bag.RemoveItem(ItemTaskType.Invalid, transfer.TransferredItem, true);
                else
                    ItemManager.Instance.ReleaseId(transfer.TransferredItem.Id);
                transfer.SourceItem.Count += transfer.Amount;
            }
            else
            {
                transfer.Source.Inventory.Bag.AddOrMoveExistingItem(
                    ItemTaskType.Invalid, transfer.TransferredItem, transfer.SourceSlot);
            }
        }
    }

    private void FinishTrade(Character owner, Character target, uint tradeId)
    {
        if (!_trades.TryGetValue(tradeId, out var tradeInfo))
            return;

        if (!owner.IsOnline || !target.IsOnline || owner.IsDead || target.IsDead ||
            owner.IsInBattle || target.IsInBattle ||
            owner.Transform.InstanceId != target.Transform.InstanceId ||
            owner.GetRelationStateTo(target) == RelationState.Hostile)
        {
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeTargetIsNotPossibleState, tradeId);
            return;
        }

        if (tradeInfo.OwnerMoneyPutup < 0 || tradeInfo.OwnerMoneyPutup > owner.Money)
        {
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeNotEnoughMoney, tradeId);
            return;
        }

        if (tradeInfo.TargetMoneyPutup < 0 || tradeInfo.TargetMoneyPutup > target.Money)
        {
            CancelTrade(target.ObjId, (int)ErrorMessageType.TradeNotEnoughMoney, tradeId);
            return;
        }

        if (!TryResolveItems(owner, tradeInfo.OwnerItems, out var ownerItems))
        {
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeInvalidItem, tradeId);
            return;
        }

        if (!TryResolveItems(target, tradeInfo.TargetItems, out var targetItems))
        {
            CancelTrade(target.ObjId, (int)ErrorMessageType.TradeInvalidItem, tradeId);
            return;
        }

        var ownerFreedSlots = ownerItems.Count(x => x.Entry.Amount == x.Item.Count);
        var targetFreedSlots = targetItems.Count(x => x.Entry.Amount == x.Item.Count);
        if (owner.Inventory.Bag.FreeSlotCount + ownerFreedSlots < targetItems.Count)
        {
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeBagFull, tradeId);
            return;
        }

        if (target.Inventory.Bag.FreeSlotCount + targetFreedSlots < ownerItems.Count)
        {
            CancelTrade(target.ObjId, (int)ErrorMessageType.TradeBagFull, tradeId);
            return;
        }

        var ownerMoneyDelta = (long)tradeInfo.TargetMoneyPutup - tradeInfo.OwnerMoneyPutup;
        var targetMoneyDelta = -ownerMoneyDelta;
        var ownerTaskCount = ownerItems.Count + targetItems.Count + (ownerMoneyDelta == 0 ? 0 : 1);
        var targetTaskCount = ownerItems.Count + targetItems.Count + (targetMoneyDelta == 0 ? 0 : 1);
        if (ownerTaskCount > ItemTaskListLimits.Tasks || targetTaskCount > ItemTaskListLimits.Tasks)
        {
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeInvalidItem, tradeId);
            return;
        }

        var tasksOwner = new List<ItemTask>(ownerTaskCount);
        var tasksTarget = new List<ItemTask>(targetTaskCount);
        var staged = new List<StagedTradeTransfer>(ownerItems.Count + targetItems.Count);
        if (!StageItems(owner, target, ownerItems, staged) ||
            !StageItems(target, owner, targetItems, staged) ||
            !CommitStagedItems(staged, tasksOwner, tasksTarget, owner))
        {
            RollbackTransfers(staged);
            CancelTrade(owner.ObjId, (int)ErrorMessageType.TradeInvalidItem, tradeId);
            return;
        }

        owner.Money += ownerMoneyDelta;
        target.Money += targetMoneyDelta;
        if (ownerMoneyDelta != 0)
            tasksOwner.Add(new MoneyChange(ownerMoneyDelta));
        if (targetMoneyDelta != 0)
            tasksTarget.Add(new MoneyChange(targetMoneyDelta));

        _trades.Remove(tradeId);
        owner.SendPacket(new SCTradeMadePacket(ItemTaskType.Trade, tasksOwner, []));
        target.SendPacket(new SCTradeMadePacket(ItemTaskType.Trade, tasksTarget, []));
        Logger.Info("Trade Id:{0} finished. Owner {1} ({2}) Items/Money: {3}/{4} <=> Target {5} ({6}) Items/Money: {7}/{8}",
            tradeId, owner.Name, owner.Id, tradeInfo.OwnerItems.Count, tradeInfo.OwnerMoneyPutup,
            target.Name, target.Id, tradeInfo.TargetItems.Count, tradeInfo.TargetMoneyPutup);
    }
}
