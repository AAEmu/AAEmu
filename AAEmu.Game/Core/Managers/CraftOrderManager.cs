using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Crafts;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The craft order board: the orders characters have posted, and the answers the board window reads.
///
/// Orders are written to <c>craft_orders</c> as soon as they are posted, cancelled, filled, or
/// they lapse, so a World kill cannot drop a listing or leave its gold escrowed twice.
/// </summary>
public class CraftOrderManager : Singleton<CraftOrderManager>, ILoadable, IInitializable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>How long an order stays on the board: the coupon table's top hour.</summary>
    public static TimeSpan ListingLifetime => CraftOrderCouponGameData.Instance.ListingLifetime;

    private readonly object _boardLock = new();
    private readonly Dictionary<ulong, CraftOrder> _orders = [];
    private readonly Dictionary<uint, List<ulong>> _ordersByOwner = [];
    private readonly Dictionary<uint, ulong> _pendingProcessOrders = [];
    private readonly Dictionary<uint, ulong> _pendingRestoreSheets = [];
    private readonly Dictionary<uint, (ulong Lowest, ulong Highest)> _feeStats = [];
    private ICraftOrderStore _store = new InMemoryCraftOrderStore();
    private ulong _nextId = 1;
    private Models.Tasks.Task _expireTask;

    /// <summary>Tests skip the expire-refund letter when MailManager is not registered.</summary>
    internal bool SkipExpiredMail { get; set; }

    /// <summary>
    /// The craft each character last asked a sheet for. The cast that makes a sheet carries the craft
    /// and the count in its own payload, so it is queued when the cast arrives and spent when the
    /// effect lands — the same shape the equip-slot reinforcement window uses for its two casts.
    /// </summary>
    private readonly Dictionary<uint, (uint CraftId, uint Count)> _pendingSheetCrafts = [];

    /// <summary>Live orders, for tests and diagnostics.</summary>
    public IReadOnlyCollection<CraftOrder> Orders
    {
        get
        {
            lock (_boardLock)
                return _orders.Values.ToList();
        }
    }

    /// <summary>
    /// GM wipe. The store goes first, then every order the board still holds is refunded from its
    /// board copy: a row that had already left MySQL is still escrow the poster paid, so it is not
    /// skipped. A wipe that fails leaves the board as it is, so memory and MySQL stay in step.
    /// </summary>
    public bool Clear()
    {
        lock (_boardLock)
        {
            var orders = _orders.Values.ToList();
            if (!_store.DeleteAll())
            {
                Logger.Error("Craft order: clear could not wipe the store; the board keeps its {0} order(s)", orders.Count);
                return false;
            }

            if (!SkipExpiredMail)
            {
                foreach (var order in orders)
                {
                    if (!TryMailExpiredRefund(order))
                        Logger.Warn("Craft order: clear could not refund order {0}", order.Id);
                }
            }

            ResetBoard();
            _pendingSheetCrafts.Clear();
            _pendingProcessOrders.Clear();
            _pendingRestoreSheets.Clear();
            if (_expireTask != null && SingletonContainer.ServiceProvider != null)
                TaskManager.Instance.Cancel(_expireTask);
            _expireTask = null;
            return true;
        }
    }

    /// <summary>Reads the persisted board. The first expiry sweep waits for MailManager (Initialize).</summary>
    public void Load()
    {
        lock (_boardLock)
        {
            _store = new MySqlCraftOrderStore();
            LoadFromStore(DateTimeOffset.UtcNow, sweep: false);
        }
    }

    /// <summary>Mails listings that lapsed while the process was down, then arms the next expiry.</summary>
    public void Initialize()
    {
        SweepExpired(DateTimeOffset.UtcNow);
    }

    internal void UseStore(ICraftOrderStore store)
    {
        lock (_boardLock)
            _store = store ?? new InMemoryCraftOrderStore();
    }

    internal void LoadFromStore(DateTimeOffset now, bool sweep = true)
    {
        ResetBoard();
        var loaded = _store.LoadAll();
        _nextId = CraftOrderPersistRules.NextId(loaded.Select(order => order.Id));
        foreach (var order in loaded)
            Track(order);

        foreach (var stat in _store.LoadFeeStats())
            _feeStats[stat.CraftId] = (stat.Lowest, stat.Highest);

        foreach (var order in _orders.Values)
            RememberFee(order.CraftId, CraftOrderFeeStatsRules.UnitFee(order.Fee, order.Count), persist: true);

        if (sweep)
            SweepExpiredNoLock(now);
        Logger.Info("Craft order: loaded {0} live order(s), {1} fee range(s)",
            _orders.Count, _feeStats.Count);
    }

    /// <summary>The orders one character has posted, newest first.</summary>
    public IReadOnlyList<CraftOrder> OwnOrders(uint characterId)
    {
        lock (_boardLock)
        {
            if (!_ordersByOwner.TryGetValue(characterId, out var ids))
                return [];

            return ids.Where(_orders.ContainsKey).Select(id => _orders[id]).Reverse().ToList();
        }
    }

    /// <summary>Drops listings whose 48 hours have run out and mails the escrow back.</summary>
    public void SweepExpired(DateTimeOffset now)
    {
        lock (_boardLock)
            SweepExpiredNoLock(now);
    }

    /// <summary>Pushes this character's own orders, which is what the board's second tab reads.</summary>
    public void SendOwnEntries(Character character)
    {
        SweepExpired(DateTimeOffset.UtcNow);
        var rows = OwnOrders(character.Id).Select(order => order.ToWireEntry()).ToList();
        if (rows.Count > CraftOrderWire.LoadEntryLimit)
        {
            // The character cannot normally exceed the cap, but never hand the client a list it would
            // silently truncate to someone else's rows.
            Logger.Warn("Craft order: {0} has {1} live orders, the client keeps {2}",
                character.Name, rows.Count, CraftOrderWire.LoadEntryLimit);
            rows = rows.Take(CraftOrderWire.LoadEntryLimit).ToList();
        }

        character.SendPacket(new SCLoadCraftOrderEntryPacket(rows));
        Logger.Info("Craft order: sent {0} own entries to {1}", rows.Count, character.Name);
    }

    /// <summary>Answers a search page from the board.</summary>
    public void SendSearch(Character character, CraftOrderQuery query)
    {
        SweepExpired(DateTimeOffset.UtcNow);
        List<CraftOrder> snapshot;
        lock (_boardLock)
            snapshot = _orders.Values.ToList();
        var matching = CraftOrderRules.Sorted(
            snapshot
                .Where(order => CraftOrderRules.MatchesFilter(order, query,
                    query.Possible ? character.Actability.GetPoint(order.ActabilityGroupId, true) : 0))
                .ToList(),
            query);

        Logger.Debug("Craft order search: group={0} kind={1} order={2} page={3} possible={4} -> {5} row(s)",
            query.ActabilityGroup, query.SortKind, query.SortOrder, query.Page, query.Possible, matching.Count);

        var page = CraftOrderRules.PageOf(matching, query.Page);
        character.SendPacket(new SCCraftOrderEntrySearchedPacket(
            (uint)matching.Count,
            query.Page,
            page.Select(order => order.ToWireEntry()).ToList()));
    }

    /// <summary>
    /// Answers the fee line of the post dialog: the cheapest and the richest unit fee posted
    /// for that craft, including listings that have already left the board.
    /// </summary>
    public void SendFeeInfo(Character character, uint craftId)
    {
        var range = FeeRange(craftId);
        character.SendPacket(new SCCraftOrderFeeInfoPacket(
            (int)craftId, range.Lowest, range.Highest, range.Any));
    }

    /// <summary>Recent listing-fee range for a craft. Empty when nothing has been posted yet.</summary>
    internal (ulong Lowest, ulong Highest, bool Any) FeeRange(uint craftId)
    {
        lock (_boardLock)
        {
            var fees = new List<ulong>();
            if (_feeStats.TryGetValue(craftId, out var stored))
            {
                fees.Add(stored.Lowest);
                fees.Add(stored.Highest);
            }

            foreach (var order in _orders.Values)
            {
                if (order.CraftId == craftId)
                    fees.Add(CraftOrderFeeStatsRules.UnitFee(order.Fee, order.Count));
            }

            return CraftOrderFeeStatsRules.FromFees(fees);
        }
    }

    /// <summary>
    /// Answers the material list 0x237 shows. The restore tab sends a sheet instance id and
    /// expects that sheet's scaled bill; the post dialog sends a product type. Restore also
    /// queues the sheet here — the confirm cast is a unit caster and does not name it again.
    /// </summary>
    public void SendMaterials(Character character, ulong itemId)
    {
        IReadOnlyList<CraftOrderMaterialRow> rows = [];
        var bagItem = character.Inventory.Bag.GetItemByItemId(itemId);
        if (CraftOrderSheetRules.RequestNamesASheet(bagItem) && bagItem is CraftOrderSheetItem sheet)
        {
            QueueRestoreSheet(character.Id, sheet.Id);
            Craft craft = null;
            if (sheet.CraftId != 0)
                CraftManager.Instance.TryGetCraft(sheet.CraftId, out craft);
            rows = CraftOrderSheetRules.MaterialRows(craft, sheet.CraftCount);
            Logger.Debug("Craft order items: sheet {0} craft {1} x{2} -> {3} row(s)",
                itemId, sheet.CraftId, sheet.CraftCount, rows.Count);
        }
        else if (CraftManager.Instance.TryFindOrderableCraftByProduct((uint)itemId, out var productCraft))
        {
            rows = CraftOrderSheetRules.MaterialRows(productCraft, 1);
            Logger.Debug("Craft order items: product {0} -> {1} row(s)", itemId, rows.Count);
        }
        else
        {
            Logger.Debug("Craft order items: item {0} matched no sheet or product", itemId);
        }

        character.SendPacket(new SCCraftOrderItemsPacket(itemId, rows));
    }

    /// <summary>Posts an order for the character, escrowing the fee it offers.</summary>
    public void Post(Character character, ulong itemId, ulong fee)
    {
        SweepExpired(DateTimeOffset.UtcNow);
        var bagItem = character.Inventory.Bag.GetItemByItemId(itemId);
        if (bagItem is not CraftOrderSheetItem sheet)
        {
            RefusePost(character, $"item {itemId} is not a request sheet");
            return;
        }

        if (!CraftManager.Instance.TryGetCraft(sheet.CraftId, out var sheetCraft) ||
            !CraftOrderRules.IsOrderable(sheetCraft))
        {
            RefusePost(character, $"sheet {itemId} has no orderable craft");
            return;
        }

        Post(character, sheetCraft, sheet.CraftCount, sheet.CraftGrade, sheet.ActabilityGroupId, fee, sheet);
    }

    private void Post(
        Character character,
        Craft craft,
        uint count,
        byte grade,
        uint actabilityGroupId,
        ulong fee,
        CraftOrderSheetItem consumeSheet)
    {
        if (!CraftOrderRules.CanPost(OwnOrders(character.Id).Count))
        {
            RefusePost(character, $"already at the {CraftOrderRules.EntriesPerCharacter} order cap");
            return;
        }

        var lifetime = ListingLifetime;
        if (lifetime <= TimeSpan.Zero)
        {
            RefusePost(character, "craft_order_coupons has no listing lifetime");
            return;
        }

        var consumeLp = SkillManager.Instance.GetSkillTemplate(craft.SkillId)?.ConsumeLaborPower ?? 0;
        var listedCount = Math.Max(1, count);
        var pcActability = character.Actability.GetPoint(craft.ActabilityGroupId, true);
        if (!CraftOrderInstantFeeRules.TryMinFee(
                FormulaManager.Instance.GetFormula((uint)FormulaKind.MinCraftOrderFee),
                craft.Cost,
                consumeLp,
                craft.ActabilityLimit,
                pcActability,
                out var minFee))
        {
            RefusePost(character, "min craft order fee cannot be evaluated");
            return;
        }

        if (!CraftOrderRules.IsListedFeeAcceptable(fee, listedCount, minFee))
        {
            RefusePost(character, $"fee {fee} / {listedCount} is below the minimum {minFee} per run");
            return;
        }

        if (!CraftOrderRules.IsEscrowable(fee))
        {
            RefusePost(character, $"fee {fee} will not fit a mail");
            return;
        }

        if (fee > 0 && !character.SubtractMoney(SlotType.Inventory, (long)fee, ItemTaskType.PostCraftOrder))
        {
            RefusePost(character, $"cannot escrow {fee} copper");
            return;
        }

        if (consumeSheet != null &&
            !character.Inventory.Bag.RemoveItem(ItemTaskType.PostCraftOrder, consumeSheet, true))
        {
            if (fee > 0)
                character.AddMoney(SlotType.Inventory, (long)fee, ItemTaskType.PostCraftOrder);
            RefusePost(character, $"cannot take sheet {consumeSheet.Id}");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var product = craft.CraftProducts[0];
        CraftOrder order;
        bool persisted;
        lock (_boardLock)
        {
            order = new CraftOrder
            {
                Id = _nextId++,
                OwnerId = character.Id,
                OwnerName = character.Name,
                OwnerWorldCharKey = CraftOrderProcessRules.InstantOwnerId(character.Id),
                CraftId = craft.Id,
                ItemId = product.ItemId,
                Grade = grade,
                Count = listedCount,
                Fee = fee,
                ActabilityGroupId = actabilityGroupId,
                ActabilityPoint = (uint)Math.Max(0, craft.ActabilityLimit),
                PostedUnix = now.ToUnixTimeSeconds(),
                ExpiresUnix = now.Add(lifetime).ToUnixTimeSeconds(),
                Status = 0,
                Kind = 0
            };

            persisted = _store.Insert(order);
            if (persisted)
            {
                Track(order);
                RememberFee(order.CraftId, CraftOrderFeeStatsRules.UnitFee(order.Fee, order.Count), persist: true);
                ArmExpireSweepNoLock();
            }
        }

        if (!persisted)
        {
            // The fee and the sheet were already taken. The sheet carries the materials, so it
            // comes back the same way a cancel hands it back.
            if (fee > 0)
                character.AddMoney(SlotType.Inventory, (long)fee, ItemTaskType.PostCraftOrder);
            ReturnSheet(character, order);
            RefusePost(character, $"cannot persist order {order.Id}");
            return;
        }

        Logger.Info("Craft order: {0} posted order {1} for item {2} x{3} at {4} copper",
            character.Name, order.Id, order.ItemId, order.Count, order.Fee);

        character.SendPacket(new SCCraftOrderActionResultPacket(CraftOrderSheetRules.PostActionKind, true));
        character.SendPacket(new SCInsertCraftOrderEntryPacket(order.ToWireEntry()));
        SendOwnEntries(character);
    }

    /// <summary>Cancels one of the character's own orders and returns the escrowed fee.</summary>
    public void Cancel(Character character, ulong orderId)
    {
        SweepExpired(DateTimeOffset.UtcNow);
        CraftOrder order;
        lock (_boardLock)
        {
            if (!_orders.TryGetValue(orderId, out order) || order.OwnerId != character.Id)
            {
                RefuseCancel(character, $"order {orderId} is not theirs");
                return;
            }
        }

        var sheet = TryCreateSheetForOrder(order);
        if (sheet == null)
        {
            RefuseCancel(character, $"cannot recreate the request sheet for order {order.Id}");
            return;
        }

        if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.RestoreCraftOrderSheet, sheet))
        {
            ItemManager.Instance.ReleaseId(sheet.Id);
            RefuseCancel(character, $"no room for the request sheet of order {order.Id}");
            return;
        }

        lock (_boardLock)
        {
            if (!_orders.ContainsKey(order.Id) || !_store.Delete(order.Id))
            {
                character.Inventory.Bag.RemoveItem(ItemTaskType.RestoreCraftOrderSheet, sheet, true);
                RefuseCancel(character, $"cannot persist cancel of order {order.Id}");
                return;
            }

            Untrack(order);
            ArmExpireSweepNoLock();
        }

        if (order.Fee > 0 && !character.AddMoney(SlotType.Inventory, (long)order.Fee, ItemTaskType.RestoreCraftOrderSheet))
        {
            lock (_boardLock)
            {
                if (_store.Insert(order))
                {
                    // Back on the board, so the expiry timer has to cover it again: a sweep that
                    // ran while the row was off the board re-armed without it.
                    Track(order);
                    ArmExpireSweepNoLock();
                }
            }

            character.Inventory.Bag.RemoveItem(ItemTaskType.RestoreCraftOrderSheet, sheet, true);
            RefuseCancel(character, $"cannot return {order.Fee} copper");
            return;
        }

        Logger.Info("Craft order: {0} cancelled order {1}", character.Name, order.Id);

        character.SendPacket(new SCCraftOrderActionResultPacket(CraftOrderSheetRules.CancelActionKind, true));
        character.SendPacket(new SCDeleteCraftOrderEntryPacket(order.Id, complete: false));
        SendOwnEntries(character);
    }

    /// <summary>Remembers the craft a sheet cast asked for, until that cast's effect lands.</summary>
    public void QueueSheetCraft(uint characterId, uint craftId, uint count)
    {
        lock (_boardLock)
            _pendingSheetCrafts[characterId] = (craftId, count);
    }

    /// <summary>Remembers the order a process cast named, until that cast's effect lands.</summary>
    public void QueueProcessOrder(uint characterId, ulong orderId)
    {
        lock (_boardLock)
            _pendingProcessOrders[characterId] = orderId;
    }

    /// <summary>Takes the order a character's process cast queued.</summary>
    public bool TryTakeProcessOrder(uint characterId, out ulong orderId)
    {
        lock (_boardLock)
        {
            if (_pendingProcessOrders.Remove(characterId, out orderId))
                return orderId != 0;
        }

        orderId = 0;
        return false;
    }

    /// <summary>Takes the craft a character's sheet cast queued, if the folio left one.</summary>
    public bool TryTakeSheetCraft(uint characterId, out uint craftId, out uint count)
    {
        lock (_boardLock)
        {
            if (_pendingSheetCrafts.Remove(characterId, out var pending))
            {
                craftId = pending.CraftId;
                count = pending.Count;
                return true;
            }
        }

        craftId = 0;
        count = 0;
        return false;
    }

    /// <summary>
    /// Makes a request sheet: bag room and the whole material bill are checked first, then the
    /// materials are consumed and the sheet is handed over. A failed add puts the materials back
    /// and releases the sheet id. Nothing is consumed when it cannot be paid.
    /// </summary>
    public bool TryCraftSheet(Character character, uint craftId, uint count, out string reason)
    {
        if (!CraftOrderSheetRules.IsUsableCount(count))
        {
            reason = $"count {count} asks for nothing";
            return false;
        }

        if (!CraftManager.Instance.TryGetCraft(craftId, out var craft) || !CraftOrderRules.IsOrderable(craft))
        {
            reason = $"craft {craftId} cannot be ordered";
            return false;
        }

        var sheetItemId = CraftOrderContent.SheetItemId;
        if (sheetItemId == 0)
        {
            reason = $"const_item_types '{CraftOrderContent.SheetItemConstName}' is missing";
            return false;
        }

        if (character.Inventory.Bag.SpaceLeftForItem(sheetItemId) < 1)
        {
            reason = "no room in the bag";
            return false;
        }

        foreach (var material in craft.CraftMaterials)
        {
            var need = CraftOrderSheetRules.MaterialCost(material.Amount, count);
            if (need == 0)
                continue;

            if (!CraftOrderSheetRules.IsConsumable(need) ||
                !character.Inventory.CheckItems(SlotType.Inventory, material.ItemId, (int)need))
            {
                reason = $"missing {need}x material {material.ItemId}";
                return false;
            }
        }

        var consumed = new List<(uint ItemId, int Count)>();
        foreach (var material in craft.CraftMaterials)
        {
            var need = CraftOrderSheetRules.MaterialCost(material.Amount, count);
            if (need == 0)
                continue;

            var taken = character.Inventory.Bag.ConsumeItem(
                ItemTaskType.MakeCraftOrderSheet, material.ItemId, (int)need, null);
            if (taken > 0)
                consumed.Add((material.ItemId, (int)taken));
            if (taken != need)
            {
                // The pass above makes this a bug rather than a player problem; say so loudly.
                Logger.Error("Craft order sheet: consumed {0} of {1} needed {2} for {3}",
                    taken, need, material.ItemId, character.Name);
                ReturnConsumedMaterials(character, consumed);
                reason = $"consumed {taken} of {need} of item {material.ItemId}";
                return false;
            }
        }

        var sheet = ItemManager.Instance.Create<CraftOrderSheetItem>(sheetItemId, 1, 0);
        if (sheet == null)
        {
            ReturnConsumedMaterials(character, consumed);
            reason = $"no item template {sheetItemId}";
            return false;
        }

        sheet.SetOrder(craftId, CraftOrderSheetRules.GradeOf(craft), count, craft.ActabilityGroupId);

        if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.MakeCraftOrderSheet, sheet))
        {
            ItemManager.Instance.ReleaseId(sheet.Id);
            ReturnConsumedMaterials(character, consumed);
            reason = "no room in the bag";
            return false;
        }

        Logger.Info("Craft order: {0} made a sheet for craft {1} x{2}", character.Name, craftId, count);
        reason = null;
        return true;
    }

    /// <summary>Remembers the sheet a restore cast named, until that cast's effect lands.</summary>
    public void QueueRestoreSheet(uint characterId, ulong sheetItemId)
    {
        lock (_boardLock)
            _pendingRestoreSheets[characterId] = sheetItemId;
    }

    /// <summary>Takes the sheet a character's restore cast queued.</summary>
    public bool TryTakeRestoreSheet(uint characterId, out ulong sheetItemId)
    {
        lock (_boardLock)
        {
            if (_pendingRestoreSheets.Remove(characterId, out sheetItemId))
                return sheetItemId != 0;
        }

        sheetItemId = 0;
        return false;
    }

    /// <summary>
    /// Breaks a request sheet back into the materials making it consumed. The sheet leaves first
    /// only after the bag has room for every stack that will come back. An invalid sheet is still
    /// destroyed and returns nothing.
    /// </summary>
    public bool TryRestoreSheet(Character character, ulong sheetItemId, out string reason)
    {
        reason = null;
        if (character == null)
        {
            reason = "no character";
            return false;
        }

        if (character.Inventory.Bag.GetItemByItemId(sheetItemId) is not CraftOrderSheetItem sheet)
        {
            reason = $"sheet {sheetItemId} is not in the bag";
            return false;
        }

        Craft craft = null;
        if (sheet.CraftId != 0)
            CraftManager.Instance.TryGetCraft(sheet.CraftId, out craft);

        var bill = CraftOrderSheetRules.RestoreMaterials(craft, sheet.CraftCount);
        foreach (var (itemId, count) in bill)
        {
            if (character.Inventory.Bag.SpaceLeftForItem(itemId) < count)
            {
                reason = $"no room for {count}x material {itemId}";
                return false;
            }
        }

        if (!character.Inventory.Bag.RemoveItem(ItemTaskType.RestoreCraftOrderSheet, sheet, true))
        {
            reason = $"cannot take sheet {sheet.Id}";
            return false;
        }

        foreach (var (itemId, count) in bill)
        {
            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RestoreCraftOrderSheet, itemId, count))
            {
                Logger.Error("Craft order restore: sheet {0} gone but could not return {1}x {2} to {3}",
                    sheetItemId, count, itemId, character.Name);
                reason = $"could not return {count}x material {itemId}";
                return false;
            }
        }

        Logger.Info("Craft order: {0} restored sheet {1} craft {2} x{3}",
            character.Name, sheetItemId, sheet.CraftId, sheet.CraftCount);
        character.SendPacket(new SCCraftOrderActionResultPacket(CraftOrderSheetRules.RestoreActionKind, true));
        return true;
    }

    /// <summary>
    /// Fills a posted order: the crafter's actability and the craft's labor are checked, labor is
    /// taken, the product is mailed to the requester and the fee after the resident charge to the
    /// crafter, then the row leaves the board. Materials were already consumed when the request
    /// sheet was made.
    /// </summary>
    public bool TryProcess(Character character, ulong orderId, out string reason)
    {
        reason = null;
        if (character == null)
        {
            reason = "no character";
            return false;
        }

        SweepExpired(DateTimeOffset.UtcNow);
        CraftOrder order;
        lock (_boardLock)
        {
            if (!_orders.TryGetValue(orderId, out order))
            {
                RefuseProcess(character, $"order {orderId} is not on the board");
                reason = $"order {orderId} is not on the board";
                return false;
            }
        }

        if (CraftOrderProcessRules.IsOwnOrder(order, character.Id))
        {
            character.SendErrorMessage(ErrorMessageType.CraftPermissionDeny);
            RefuseProcess(character, $"order {order.Id} is their own");
            reason = "own order";
            return false;
        }

        if (!CraftManager.Instance.TryGetCraft(order.CraftId, out var craft) ||
            craft.CraftProducts.Count == 0)
        {
            RefuseProcess(character, $"order {order.Id} has no craft");
            reason = $"craft {order.CraftId} missing";
            return false;
        }

        var actability = character.Actability.GetPoint(order.ActabilityGroupId, true);
        Logger.Info(
            "Craft order: {0} filling order {1} craft {2} actability {3}/{4}",
            character.Name, order.Id, order.CraftId, actability, order.ActabilityPoint);

        if (!CraftOrderProcessRules.CanProcess(order, character.Id, actability))
        {
            character.SendErrorMessage(ErrorMessageType.ActabilityNotEnoughPoint);
            RefuseProcess(character, $"actability {actability} below {order.ActabilityPoint}");
            reason = "actability";
            return false;
        }

        var productCount = CraftOrderProcessRules.ProductCount(craft.CraftProducts[0].Amount, order.Count);
        if (productCount <= 0)
        {
            RefuseProcess(character, $"order {order.Id} produces nothing");
            reason = "no product";
            return false;
        }

        if (!CraftOrderContent.TryChargePermille(out var permille))
        {
            RefuseProcess(character, $"content_configs '{CraftOrderContent.ChargeConfigName}' is missing");
            reason = "charge config";
            return false;
        }
        var payout = CraftOrderProcessRules.CrafterPayout(order.Fee, permille);
        if (payout > int.MaxValue)
        {
            RefuseProcess(character, $"order {order.Id} payout {payout} will not fit a mail");
            reason = "fee overflow";
            return false;
        }

        var ownerName = NameManager.Instance.GetCharacterName(order.OwnerId) ?? order.OwnerName;
        if (string.IsNullOrWhiteSpace(ownerName) || string.IsNullOrWhiteSpace(character.Name))
        {
            RefuseProcess(character, $"order {order.Id} has no mail names");
            reason = "no names";
            return false;
        }

        var skillTemplate = SkillManager.Instance.GetSkillTemplate(craft.SkillId);
        Skill laborSkill = null;
        if (skillTemplate is { ConsumeLaborPower: > 0 })
        {
            var units = order.Count > int.MaxValue ? int.MaxValue : Math.Max(1, (int)order.Count);
            laborSkill = new Skill(skillTemplate) { LaborUnits = units };
            if (!laborSkill.CanAffordLabor(character))
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                RefuseProcess(character, $"not enough labor for {laborSkill.GetLaborCost(character)}");
                reason = "labor";
                return false;
            }
        }

        var product = ItemManager.Instance.Create(order.ItemId, productCount, order.Grade);
        if (product == null)
        {
            RefuseProcess(character, $"cannot create item {order.ItemId} x{productCount}");
            reason = "no item";
            return false;
        }

        var mails = new List<BaseMail>
        {
            MailForCraftOrder.ForCompletedOrder(order.OwnerId, ownerName, order.CraftId, product)
        };
        if (payout > 0)
            mails.Add(MailForCraftOrder.ForProcessFee(character.Id, character.Name, order.CraftId, (int)payout));

        var laborCost = laborSkill?.GetLaborCost(character) ?? 0;
        if (laborSkill != null && !laborSkill.TryConsumeLabor(character))
        {
            ItemManager.Instance.ReleaseId(product.Id);
            character.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            RefuseProcess(character, $"cannot take {laborCost} labor");
            reason = "labor consume";
            return false;
        }

        lock (_boardLock)
        {
            if (!_orders.ContainsKey(order.Id))
            {
                RestoreLabor(character, laborCost, skillTemplate?.ActabilityGroupId ?? 0);
                ItemManager.Instance.ReleaseId(product.Id);
                RefuseProcess(character, $"order {order.Id} left the board");
                reason = "gone";
                return false;
            }

            if (!_store.Delete(order.Id))
            {
                RestoreLabor(character, laborCost, skillTemplate?.ActabilityGroupId ?? 0);
                ItemManager.Instance.ReleaseId(product.Id);
                RefuseProcess(character, $"cannot persist fill of order {order.Id}");
                reason = "persist";
                return false;
            }

            Untrack(order);
        }

        if (!MailManager.Instance.SendBatch(mails))
        {
            RestoreLabor(character, laborCost, skillTemplate?.ActabilityGroupId ?? 0);
            ItemManager.Instance.ReleaseId(product.Id);
            lock (_boardLock)
            {
                if (_store.Insert(order))
                {
                    // Back on the board, so the expiry timer has to cover it again: a sweep that
                    // ran while the row was off the board re-armed without it.
                    Track(order);
                    ArmExpireSweepNoLock();
                }
            }

            RefuseProcess(character, $"cannot mail order {order.Id}");
            reason = "mail";
            return false;
        }
        Logger.Info(
            "Craft order: {0} filled order {1} for {2}: item {3} x{4} mailed, fee {5} copper, payout {6}, cut {7} permille={8}, labor {9}",
            character.Name, order.Id, ownerName, order.ItemId, productCount, order.Fee, payout,
            CraftOrderProcessRules.ChargeCut(order.Fee, permille), permille, laborCost);

        var entry = order.ToWireEntry();
        var owner = WorldManager.Instance.GetCharacterById(order.OwnerId);
        owner?.SendPacket(new SCCompleteCraftOrderEntryPacket(entry));
        owner?.SendPacket(new SCDeleteCraftOrderEntryPacket(order.Id, complete: true));
        if (owner != null)
            SendOwnEntries(owner);

        character.SendPacket(new SCCraftOrderActionResultPacket(CraftOrderProcessRules.ProcessActionKind, true));
        character.SendPacket(new SCDeleteCraftOrderEntryPacket(order.Id, complete: true));
        return true;
    }

    /// <summary>
    /// Owner Instant complete: tickets plus the additional-fee formulas, product mailed to the
    /// owner, listing escrow returned. There is no crafter payout.
    /// </summary>
    public bool TryProcessInstant(Character character, ulong orderId, out string reason)
    {
        reason = null;
        if (character == null)
        {
            reason = "no character";
            return false;
        }

        SweepExpired(DateTimeOffset.UtcNow);
        CraftOrder order;
        lock (_boardLock)
        {
            if (!_orders.TryGetValue(orderId, out order))
            {
                RefuseInstant(character, $"order {orderId} is not on the board");
                reason = $"order {orderId} is not on the board";
                return false;
            }
        }

        if (!CraftOrderProcessRules.CanInstant(order, character.Id))
        {
            character.SendErrorMessage(ErrorMessageType.CraftPermissionDeny);
            RefuseInstant(character, $"order {order.Id} is not theirs");
            reason = "not owner";
            return false;
        }

        if (!CraftManager.Instance.TryGetCraft(order.CraftId, out var craft) ||
            craft.CraftProducts.Count == 0)
        {
            RefuseInstant(character, $"order {order.Id} has no craft");
            reason = $"craft {order.CraftId} missing";
            return false;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hours = CraftOrderCouponRules.RemainingHours(order.ExpiresUnix, nowUnix);
        if (!CraftOrderCouponRules.TryCost(CraftOrderCouponGameData.Instance.Coupons, hours, out var ticketId, out var ticketCount))
        {
            character.SendErrorMessage(ErrorMessageType.CraftInvalidCraftType);
            RefuseInstant(character, $"no coupon band for {hours} remaining hour(s)");
            reason = "no coupon band";
            return false;
        }

        if (!character.Inventory.CheckItems(SlotType.Inventory, ticketId, ticketCount))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            RefuseInstant(character, $"missing {ticketCount}x ticket {ticketId}");
            reason = "tickets";
            return false;
        }

        if (SkillManager.Instance.GetSkillTemplate(CraftOrderContent.InstantSkillId) == null)
        {
            RefuseInstant(character, $"const_skill_types '{CraftOrderContent.InstantSkillConstName}' is missing");
            reason = "instant skill";
            return false;
        }

        var consumeLp = SkillManager.Instance.GetSkillTemplate(craft.SkillId)?.ConsumeLaborPower ?? 0;
        if (!CraftOrderInstantFeeRules.TryAdditionalFee(
                FormulaManager.Instance.GetFormula((uint)FormulaKind.MinCraftOrderFee),
                FormulaManager.Instance.GetFormula((uint)FormulaKind.CraftOrderAdditionalFee),
                craft.Cost,
                consumeLp,
                craft.ActabilityLimit,
                CraftOrderInstantFeeRules.InstantPcActability(order.Grade),
                order.Count,
                out var additionalFee))
        {
            RefuseInstant(character, $"order {order.Id} additional fee cannot be evaluated");
            reason = "additional fee";
            return false;
        }

        if (additionalFee > 0 &&
            !character.SubtractMoney(SlotType.Inventory, additionalFee, ItemTaskType.SkillReagents))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            RefuseInstant(character, $"cannot pay {additionalFee} copper additional fee");
            reason = "gold";
            return false;
        }

        var taken = character.Inventory.Bag.ConsumeItem(
            ItemTaskType.SkillReagents, ticketId, ticketCount, null);
        if (taken != ticketCount)
        {
            if (additionalFee > 0)
                character.AddMoney(SlotType.Inventory, additionalFee, ItemTaskType.SkillReagents);
            RefuseInstant(character, $"consumed {taken} of {ticketCount} ticket {ticketId}");
            reason = "ticket consume";
            return false;
        }

        var productCount = CraftOrderProcessRules.ProductCount(craft.CraftProducts[0].Amount, order.Count);
        if (productCount <= 0)
        {
            RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
            RefuseInstant(character, $"order {order.Id} produces nothing");
            reason = "no product";
            return false;
        }

        var ownerName = NameManager.Instance.GetCharacterName(order.OwnerId) ?? order.OwnerName;
        if (string.IsNullOrWhiteSpace(ownerName))
        {
            RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
            RefuseInstant(character, $"order {order.Id} has no mail name");
            reason = "no name";
            return false;
        }

        var product = ItemManager.Instance.Create(order.ItemId, productCount, order.Grade);
        if (product == null)
        {
            RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
            RefuseInstant(character, $"cannot create item {order.ItemId} x{productCount}");
            reason = "no item";
            return false;
        }

        lock (_boardLock)
        {
            if (!_orders.ContainsKey(order.Id))
            {
                RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
                ItemManager.Instance.ReleaseId(product.Id);
                RefuseInstant(character, $"order {order.Id} left the board");
                reason = "gone";
                return false;
            }

            if (!_store.Delete(order.Id))
            {
                RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
                ItemManager.Instance.ReleaseId(product.Id);
                RefuseInstant(character, $"cannot persist instant of order {order.Id}");
                reason = "persist";
                return false;
            }

            Untrack(order);
        }

        var mails = new List<BaseMail>
        {
            MailForCraftOrder.ForCompletedOrder(order.OwnerId, ownerName, order.CraftId, product)
        };
        if (!MailManager.Instance.SendBatch(mails))
        {
            RestoreInstantCost(character, ticketId, ticketCount, additionalFee);
            ItemManager.Instance.ReleaseId(product.Id);
            lock (_boardLock)
            {
                if (_store.Insert(order))
                {
                    // Back on the board, so the expiry timer has to cover it again: a sweep that
                    // ran while the row was off the board re-armed without it.
                    Track(order);
                    ArmExpireSweepNoLock();
                }
            }

            RefuseInstant(character, $"cannot mail order {order.Id}");
            reason = "mail";
            return false;
        }

        if (order.Fee > 0 && !character.AddMoney(SlotType.Inventory, (long)order.Fee, ItemTaskType.RestoreCraftOrderSheet))
        {
            Logger.Error("Craft order instant: mailed product for {0} but could not return {1} copper escrow",
                order.Id, order.Fee);
        }

        Logger.Info(
            "Craft order: {0} instant-completed order {1}: item {2} x{3} mailed, tickets {4}x{5}, additionalFee {6}, escrow {7} returned, hours {8}",
            character.Name, order.Id, order.ItemId, productCount, ticketCount, ticketId, additionalFee, order.Fee, hours);

        character.SendPacket(new SCCraftOrderActionResultPacket(CraftOrderProcessRules.InstantActionKind, true));
        character.SendPacket(new SCCompleteCraftOrderEntryPacket(order.ToWireEntry()));
        character.SendPacket(new SCDeleteCraftOrderEntryPacket(order.Id, complete: true));
        SendOwnEntries(character);
        return true;
    }

    private static void RestoreInstantCost(Character character, uint ticketId, int ticketCount, int additionalFee)
    {
        if (ticketCount > 0)
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillReagents, ticketId, ticketCount);
        if (additionalFee > 0)
            character.AddMoney(SlotType.Inventory, additionalFee, ItemTaskType.SkillReagents);
    }

    private void ResetBoard()
    {
        _orders.Clear();
        _ordersByOwner.Clear();
        _feeStats.Clear();
        _nextId = 1;
    }

    /// <summary>
    /// Records a posted unit fee so the next sheet pick still has a lowest / highest after
    /// this listing leaves the board.
    /// </summary>
    private void RememberFee(uint craftId, ulong fee, bool persist)
    {
        if (craftId == 0)
            return;

        var have = _feeStats.TryGetValue(craftId, out var cur);
        var next = CraftOrderFeeStatsRules.Include(cur.Lowest, cur.Highest, have, fee);
        if (have && next.Lowest == cur.Lowest && next.Highest == cur.Highest)
            return;

        _feeStats[craftId] = next;
        if (persist)
            _store.UpsertFeeStats(new CraftOrderFeeStat(craftId, next.Lowest, next.Highest));
    }

    private void SweepExpiredNoLock(DateTimeOffset now)
    {
        var nowUnix = now.ToUnixTimeSeconds();
        var expired = _orders.Values.Where(order => CraftOrderPersistRules.IsExpired(order, nowUnix)).ToList();
        foreach (var order in expired)
        {
            if (!_store.Delete(order.Id))
            {
                // Either the row is gone behind the board's back or the store is down. The two are
                // not told apart, so the order stays on the board and the retry shows in the log.
                Logger.Warn("Craft order: expired order {0} could not be deleted from the store; retrying in {1}",
                    order.Id, CraftOrderPersistRules.ExpireRetry);
                continue;
            }

            Untrack(order);
            if (!TryMailExpiredRefund(order))
            {
                if (_store.Insert(order))
                {
                    Track(order);
                    Logger.Warn("Craft order: expired order {0} for {1} could not be refunded by mail; retrying in {2}",
                        order.Id, order.OwnerName, CraftOrderPersistRules.ExpireRetry);
                }
                else
                {
                    Logger.Error("Craft order: expired order {0} left the store and the refund mail failed", order.Id);
                }

                continue;
            }

            Logger.Info("Craft order: expired order {0} for {1}, refunded {2} copper",
                order.Id, order.OwnerName, order.Fee);
            if (SingletonContainer.ServiceProvider == null)
                continue;

            var owner = WorldManager.Instance.GetCharacterById(order.OwnerId);
            owner?.SendPacket(new SCDeleteCraftOrderEntryPacket(order.Id, complete: false));
            if (owner != null)
                SendOwnEntriesNoSweep(owner);
        }

        ArmExpireSweepNoLock();
    }

    private void ArmExpireSweepNoLock()
    {
        if (SingletonContainer.ServiceProvider == null)
            return;

        if (_expireTask != null)
        {
            TaskManager.Instance.Cancel(_expireTask);
            _expireTask = null;
        }

        var delay = CraftOrderPersistRules.NextSweepDelay(
            DateTimeOffset.UtcNow, _orders.Values.Select(order => order.ExpiresUnix));
        if (delay <= TimeSpan.Zero)
            return;

        _expireTask = new CraftOrderExpireTask();
        TaskManager.Instance.Schedule(_expireTask, delay);
    }

    private void SendOwnEntriesNoSweep(Character character)
    {
        var rows = OwnOrders(character.Id).Select(order => order.ToWireEntry()).ToList();
        if (rows.Count > CraftOrderWire.LoadEntryLimit)
            rows = rows.Take(CraftOrderWire.LoadEntryLimit).ToList();
        character.SendPacket(new SCLoadCraftOrderEntryPacket(rows));
    }

    private bool TryMailExpiredRefund(CraftOrder order)
    {
        if (SkipExpiredMail)
            return true;
        if (!CraftOrderRules.IsEscrowable(order.Fee))
            return false;

        var ownerName = NameManager.Instance.GetCharacterName(order.OwnerId) ?? order.OwnerName;
        if (string.IsNullOrWhiteSpace(ownerName))
            return false;

        var sheet = TryCreateSheetForOrder(order);
        if (order.Fee == 0 && sheet == null)
            return true;

        var mail = MailForCraftOrder.ForExpiredRefund(
            order.OwnerId, ownerName, order.CraftId, (int)order.Fee, sheet);
        if (MailManager.Instance.SendBatch([mail]))
            return true;

        if (sheet != null)
            ItemManager.Instance.ReleaseId(sheet.Id);
        return false;
    }

    /// <summary>
    /// Hands the request sheet back for an order that did not make it onto the board. The slot the
    /// sheet just left is still free, so a failed add is an item problem and is logged as one.
    /// </summary>
    private static void ReturnSheet(Character character, CraftOrder order)
    {
        var sheet = TryCreateSheetForOrder(order);
        if (sheet == null)
        {
            Logger.Error("Craft order: could not recreate the request sheet of order {0} for {1}",
                order.Id, character.Name);
            return;
        }

        if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.RestoreCraftOrderSheet, sheet))
        {
            ItemManager.Instance.ReleaseId(sheet.Id);
            Logger.Error("Craft order: could not return the request sheet of order {0} to {1}",
                order.Id, character.Name);
        }
    }

    private static CraftOrderSheetItem TryCreateSheetForOrder(CraftOrder order)
    {
        var sheetItemId = CraftOrderContent.SheetItemId;
        if (sheetItemId == 0 || order == null)
            return null;

        var sheet = ItemManager.Instance.Create<CraftOrderSheetItem>(sheetItemId, 1, 0);
        if (sheet == null)
            return null;

        sheet.SetOrder(order.CraftId, order.Grade, Math.Max(1, order.Count), order.ActabilityGroupId);
        return sheet;
    }

    private static void ReturnConsumedMaterials(Character character, IReadOnlyList<(uint ItemId, int Count)> consumed)
    {
        foreach (var (itemId, amount) in consumed)
        {
            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.MakeCraftOrderSheet, itemId, amount))
            {
                Logger.Error("Craft order sheet: could not return {0}x {1} to {2}",
                    amount, itemId, character.Name);
            }
        }
    }

    private static void RestoreLabor(Character character, int laborCost, int actabilityGroupId)
    {
        if (laborCost > 0 && laborCost <= short.MaxValue)
            character.ChangeLabor((short)laborCost, actabilityGroupId);
    }

    internal void ImportForTest(CraftOrder order)
    {
        lock (_boardLock)
        {
            _store.Insert(order);
            Track(order);
            RememberFee(order.CraftId, order.Fee, persist: true);
            if (order.Id >= _nextId)
                _nextId = order.Id + 1;
        }
    }

    private void Track(CraftOrder order)
    {
        _orders[order.Id] = order;
        if (!_ordersByOwner.TryGetValue(order.OwnerId, out var ids))
        {
            ids = [];
            _ordersByOwner[order.OwnerId] = ids;
        }

        ids.Add(order.Id);
    }

    private void Untrack(CraftOrder order)
    {
        _orders.Remove(order.Id);
        if (_ordersByOwner.TryGetValue(order.OwnerId, out var ids))
            ids.Remove(order.Id);
    }

    private static void RefusePost(Character character, string reason) =>
        Refuse(character, reason, CraftOrderSheetRules.PostActionKind);

    private static void RefuseCancel(Character character, string reason) =>
        Refuse(character, reason, CraftOrderSheetRules.CancelActionKind);

    private static void RefuseProcess(Character character, string reason) =>
        Refuse(character, reason, CraftOrderProcessRules.ProcessActionKind);

    private static void RefuseInstant(Character character, string reason) =>
        Refuse(character, reason, CraftOrderProcessRules.InstantActionKind);

    private static void Refuse(Character character, string reason, byte kind)
    {
        Logger.Info("Craft order refused for {0}: {1}", character?.Name ?? "?", reason);
        character?.SendPacket(new SCCraftOrderActionResultPacket(kind, false));
    }
}
