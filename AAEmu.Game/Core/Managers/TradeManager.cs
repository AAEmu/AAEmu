using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TradeTemplate
    {
        public uint Id { get; set; }
        public uint OwnerObjId { get; set; }
        public uint TargetObjId { get; set; }
        public bool LockOwner { get; set; }
        public bool LockTarget { get; set; }
        public bool OkOwner { get; set; }
        public bool OkTarget { get; set; }
        public List<Item> OwnerItems { get; set; }
        public List<Item> TargetItems { get; set; }
        public int OwnerMoneyPutup { get; set; }
        public int TargetMoneyPutup { get; set; }
    }

    public class TradeManager : Singleton<TradeManager>
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<uint, TradeTemplate> _trades;

        public TradeManager()
        {
            _trades = new Dictionary<uint, TradeTemplate>();
        }

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
            var tradeId = GetTradeId(objId);
            if (tradeId == 0) return false;

            CancelTrade(objId, 0, tradeId); // TODO - reason?
            return true;
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
            _log.Info("Trade Id:{0} Lockers opened and Ok undone.", tradeId);
        }

        public void CanStartTrade(Character owner, Character target)
        {
            if (IsTrading(owner.ObjId) || IsTrading(target.ObjId)) return;

            // TODO - Check faction and others
            _log.Info("{0}({1}) is trying to trade with {2}({3}).", owner.Name, owner.ObjId, target.Name, target.ObjId);
            target.SendPacket(new SCCanStartTradePacket(owner.ObjId));
        }

        public void StartTrade(Character owner, Character target)
        {
            if (IsTrading(owner.ObjId) || IsTrading(target.ObjId)) return;

            var nextId = TradeIdManager.Instance.GetNextId();
            var template = new TradeTemplate
            {
                Id = nextId,
                OwnerObjId = owner.ObjId,
                TargetObjId = target.ObjId,
                LockOwner = false,
                LockTarget = false,
                OkOwner = false,
                OkTarget = false,
                OwnerItems = new List<Item>(),
                TargetItems = new List<Item>(),
                OwnerMoneyPutup = 0,
                TargetMoneyPutup = 0

            };
            _trades.Add(nextId, template);

            _log.Info("Trade Id:{4} started between {0}({1}) - {2}({3}).", owner.Name, owner.ObjId, target.Name, target.ObjId, nextId);
            owner.SendPacket(new SCTradeStartedPacket(target.ObjId));
            target.SendPacket(new SCTradeStartedPacket(owner.ObjId));
        }

        public void CancelTrade(uint objId, int reason, uint tradeId = 0u)
        {
            // TODO - All reasons.
            tradeId = tradeId == 0 ? GetTradeId(objId) : tradeId;
            if (tradeId == 0)
            {
                WorldManager.Instance.GetCharacterByObjId(objId)?.SendPacket(new SCTradeCanceledPacket(reason, true));
                return;
            }

            var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
            var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);
            _trades.Remove(tradeId);

            _log.Info("Trade Id:{4} between {0}({1}) - {2}({3}) is canceled.", owner.Name, owner.ObjId, target.Name, target.ObjId, tradeId);
            var causedByMe = owner.ObjId.Equals(objId);
            owner.SendPacket(new SCTradeCanceledPacket(reason, causedByMe));
            target.SendPacket(new SCTradeCanceledPacket(reason, !causedByMe));
        }

        public void AddItem(Character character, SlotType slotType, byte slot, int amount)
        {
            var tradeId = GetTradeId(character.ObjId);
            var item = character.Inventory.GetItem(slotType, slot);
            if (tradeId != 0 && item.Count >= amount)
            {
                var isOwnerWhoAdd = _trades[tradeId].OwnerObjId.Equals(character.ObjId);
                var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
                var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);
                if (isOwnerWhoAdd)
                {
                    _log.Info("Trade Id:{0} {1}({2}) added item ({3}-{4}) Amount: {5}.", tradeId, owner.Name, owner.ObjId, slotType, slot, amount);
                    _trades[tradeId].OwnerItems.Add(item);
                    owner.SendPacket(new SCTradeItemPutupPacket(slotType, slot, amount));
                    target.SendPacket(new SCOtherTradeItemPutupPacket(item));
                }
                else
                {
                    _log.Info("Trade Id:{0} {1}({2}) added item ({3}-{4}) Amount: {5}.", tradeId, target.Name, target.ObjId, slotType, slot, amount);
                    _trades[tradeId].TargetItems.Add(item);
                    owner.SendPacket(new SCOtherTradeItemPutupPacket(item));
                    target.SendPacket(new SCTradeItemPutupPacket(slotType, slot, amount));
                }

                // If trade was Locked, unlock both
                UnlockTrade(owner, target, tradeId);
            }
            else
            {
                CancelTrade(character.ObjId, 0, tradeId); // TODO - Reason
            }
        }

        public void AddMoney(Character character, int moneyAmount)
        {
            var tradeId = GetTradeId(character.ObjId);
            if (tradeId != 0 && character.Money >= moneyAmount)
            {
                var isOwnerWhoAdd = _trades[tradeId].OwnerObjId.Equals(character.ObjId);
                var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
                var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);
                if (isOwnerWhoAdd)
                {
                    _log.Info("Trade Id:{0} {1}({2}) changed Money: {3}.", tradeId, owner.Name, owner.ObjId, moneyAmount);
                    _trades[tradeId].OwnerMoneyPutup = moneyAmount;
                    owner.SendPacket(new SCTradeMoneyPutupPacket(moneyAmount));
                    target.SendPacket(new SCOtherTradeMoneyPutupPacket(moneyAmount));
                }
                else
                {
                    _log.Info("Trade Id:{0} {1}({2}) changed Money: {3}.", tradeId, target.Name, target.ObjId, moneyAmount);
                    _trades[tradeId].TargetMoneyPutup = moneyAmount;
                    owner.SendPacket(new SCOtherTradeMoneyPutupPacket(moneyAmount));
                    target.SendPacket(new SCTradeMoneyPutupPacket(moneyAmount));
                }

                // If trade was Locked, unlock both
                UnlockTrade(owner, target, tradeId);
            }
            else
            {
                CancelTrade(character.ObjId, 0, tradeId); // TODO - Reason
            }
        }

        public void RemoveItem(Character character, SlotType slotType, byte slot)
        {
            var tradeId = GetTradeId(character.ObjId);
            var item = character.Inventory.GetItem(slotType, slot);
            if (tradeId != 0 && item != null)
            {
                var isOwnerWhoAdd = _trades[tradeId].OwnerObjId.Equals(character.ObjId);
                var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
                var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);
                if (isOwnerWhoAdd)
                {
                    _log.Info("Trade Id:{0} {1}({2}) tookdown item ({3}-{4}).", tradeId, owner.Name, owner.ObjId, slotType, slot);
                    if (_trades[tradeId].OwnerItems.Count <= 1) _trades[tradeId].OwnerItems.Clear();
                    else _trades[tradeId].OwnerItems.Remove(item);
                    owner.SendPacket(new SCTradeItemTookdownPacket(slotType, slot));
                    target.SendPacket(new SCOtherTradeItemTookdownPacket(item));
                }
                else
                {
                    _log.Info("Trade Id:{0} {1}({2}) tookdown item ({3}-{4}).", tradeId, target.Name, target.ObjId, slotType, slot);
                    if (_trades[tradeId].TargetItems.Count <= 1) _trades[tradeId].TargetItems.Clear();
                    else _trades[tradeId].TargetItems.Remove(item);
                    owner.SendPacket(new SCOtherTradeItemTookdownPacket(item));
                    target.SendPacket(new SCTradeItemTookdownPacket(slotType, slot));
                }

                // If trade was Locked, unlock both
                UnlockTrade(owner, target, tradeId);
            }
            else
            {
                CancelTrade(character.ObjId, 0, tradeId); // TODO - Reason
            }
        }

        public void LockTrade(Character character, bool _lock)
        {
            var tradeId = GetTradeId(character.ObjId);
            if (tradeId != 0)
            {
                var isOwnerWhoAdd = _trades[tradeId].OwnerObjId.Equals(character.ObjId);

                // Check if already locked
                if (isOwnerWhoAdd && _trades[tradeId].LockOwner && _lock) return;
                if (!isOwnerWhoAdd && _trades[tradeId].LockTarget && _lock) return;

                var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
                var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);

                if (!_lock)
                {
                    _trades[tradeId].LockOwner = false;
                    _trades[tradeId].LockTarget = false;
                    _log.Info("Trade Id:{0} {1}({2}) - {3}({4}) unlocked trade.", tradeId, owner.Name, owner.ObjId, target.Name, target.ObjId);
                }
                else if (isOwnerWhoAdd)
                {

                    _trades[tradeId].LockOwner = true;
                    _log.Info("Trade Id:{0} {1}({2}) locked trade.", tradeId, owner.Name, owner.ObjId);
                }
                else
                {
                    _trades[tradeId].LockTarget = true;
                    _log.Info("Trade Id:{0} {1}({2}) locked trade.", tradeId, target.Name, target.ObjId);
                }

                owner.SendPacket(new SCTradeLockUpdatePacket(_trades[tradeId].LockOwner, _trades[tradeId].LockTarget));
                target.SendPacket(new SCTradeLockUpdatePacket(_trades[tradeId].LockTarget, _trades[tradeId].LockOwner));
            }
            else
            {
                CancelTrade(character.ObjId, 0, tradeId); // TODO - Reason
            }
        }

        public void OkTrade(Character character)
        {
            var tradeId = GetTradeId(character.ObjId);
            if (tradeId != 0)
            {
                var isOwnerWhoAdd = _trades[tradeId].OwnerObjId.Equals(character.ObjId);
                // Check if both locked
                if (!_trades[tradeId].LockOwner && !_trades[tradeId].LockTarget) return;

                var owner = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].OwnerObjId);
                var target = WorldManager.Instance.GetCharacterByObjId(_trades[tradeId].TargetObjId);

                if (isOwnerWhoAdd)
                {

                    _trades[tradeId].OkOwner = true;
                    _log.Info("Trade Id:{0} {1}({2}) ok trade.", tradeId, owner.Name, owner.ObjId);
                }
                else
                {
                    _trades[tradeId].OkTarget = true;
                    _log.Info("Trade Id:{0} {1}({2}) ok trade.", tradeId, target.Name, target.ObjId);
                }

                // Send ok status
                owner.SendPacket(new SCTradeOkUpdatePacket(_trades[tradeId].OkOwner, _trades[tradeId].OkTarget));
                target.SendPacket(new SCTradeOkUpdatePacket(_trades[tradeId].OkTarget, _trades[tradeId].OkOwner));

                // If both ok finish trade
                if (_trades[tradeId].OkOwner && _trades[tradeId].OkTarget)
                {
                    // Check inventory space
                    if (owner.Inventory.FreeSlotCount(SlotType.Inventory) < _trades[tradeId].TargetItems.Count) CancelTrade(owner.ObjId, 0, tradeId);
                    if (target.Inventory.FreeSlotCount(SlotType.Inventory) < _trades[tradeId].OwnerItems.Count) CancelTrade(target.ObjId, 0, tradeId);

                    // Finish trade
                    FinishTrade(owner, target, tradeId);
                }
            }
            else
            {
                CancelTrade(character.ObjId, 0, tradeId); // TODO - Reason
            }
        }

        public void FinishTrade(Character owner, Character target, uint tradeId)
        {
            var tradeInfo = _trades[tradeId];

            // TODO: check if this actually works correctly with the items-rewrite with the way packets are handled

            /*
             * TODO -
             * try catch
             * check if bound
             * check untradable
             */
            var tasksOwner = new List<ItemTask>();
            var tasksTarget = new List<ItemTask>();
            // Handle Money from Owner
            if (tradeInfo.OwnerMoneyPutup > 0)
            {
                owner.Money -= tradeInfo.OwnerMoneyPutup;
                tasksOwner.Add(new MoneyChange(-tradeInfo.OwnerMoneyPutup));
                target.Money += tradeInfo.OwnerMoneyPutup;
                tasksTarget.Add(new MoneyChange(tradeInfo.OwnerMoneyPutup));
            }

            // Handle Money from Target
            if (tradeInfo.TargetMoneyPutup > 0)
            {
                owner.Money += tradeInfo.TargetMoneyPutup;
                tasksOwner.Add(new MoneyChange(tradeInfo.TargetMoneyPutup));
                target.Money -= tradeInfo.TargetMoneyPutup;
                tasksTarget.Add(new MoneyChange(-tradeInfo.TargetMoneyPutup));
            }

            // Handle Items from Owner
            if (tradeInfo.OwnerItems.Count > 0)
            {
                foreach (var item in tradeInfo.OwnerItems)
                {
                    target.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
                    //owner.Inventory.RemoveItem(item, false);
                    tasksOwner.Add(new ItemRemove(item));
                    //item.Slot = -1;
                    //var newItem = target.Inventory.AddItem(item);
                    //tasksTarget.Add(new ItemAdd(newItem));
                    tasksTarget.Add(new ItemAdd(item));
                }
            }
            // Handle Items from Target
            if (tradeInfo.TargetItems.Count > 0)
            {
                foreach (var item in tradeInfo.TargetItems)
                {
                    owner.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
                    //target.Inventory.RemoveItem(item, false);
                    tasksTarget.Add(new ItemRemove(item));
                    //item.Slot = -1;
                    //var newItem = owner.Inventory.AddItem(item);
                    // tasksOwner.Add(new ItemAdd(newItem));
                    tasksOwner.Add(new ItemAdd(item));
                }
            }

            _trades.Remove(tradeId);
            owner.SendPacket(new SCTradeMadePacket(ItemTaskType.Trade, tasksOwner, new List<ulong>()));
            target.SendPacket(new SCTradeMadePacket(ItemTaskType.Trade, tasksTarget, new List<ulong>()));
            _log.Info("Trade Id:{0} finished. Owner Items/Money: {1}/{2}. Target Items/Money: {3}/{4}",
                tradeId, tradeInfo.OwnerItems.Count, tradeInfo.OwnerMoneyPutup, tradeInfo.TargetItems.Count, tradeInfo.TargetMoneyPutup);
        }
    }
}
