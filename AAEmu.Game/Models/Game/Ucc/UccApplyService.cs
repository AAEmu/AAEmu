using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using NLog;

namespace AAEmu.Game.Models.Game.Ucc;

public enum UccApplyOutcome
{
    /// <summary>The request was authorized, paid for and written.</summary>
    Applied,

    /// <summary>Everything the request asked for is already in that state, so nothing was consumed.</summary>
    NoChange,

    /// <summary>The configured material row for this apply kind is absent or invalid; the request is skipped.</summary>
    MissingMaterialConfig,

    /// <summary>The sender does not own the source, the target, or the house.</summary>
    Unauthorized,

    /// <summary>A referenced object does not exist.</summary>
    NotFound,

    /// <summary>The request body is malformed or carries no usable placement.</summary>
    InvalidRequest,

    /// <summary>The sender does not hold the configured material; nothing was consumed.</summary>
    InsufficientMaterial,
}

/// <summary>Outcome of applying one UCC to a batch of the sender's items.</summary>
public sealed class UccItemApplyResult
{
    public UccItemApplyResult(UccApplyOutcome outcome, string reason)
    {
        Outcome = outcome;
        Reason = reason;
    }

    public UccApplyOutcome Outcome { get; }
    public string Reason { get; }

    /// <summary>The UCC that the authorized source item carries.</summary>
    public ulong UccId { get; init; }

    /// <summary>Items whose UCC changed; empty unless <see cref="Outcome"/> is <see cref="UccApplyOutcome.Applied"/>.</summary>
    public List<Item> ChangedItems { get; } = [];

    public uint ConsumedItemId { get; set; }
    public int ConsumedCount { get; set; }

    public bool Success => Outcome == UccApplyOutcome.Applied;
}

/// <summary>Outcome of applying or removing one UCC on a house slot.</summary>
public sealed class UccHousingApplyResult
{
    public UccHousingApplyResult(UccApplyOutcome outcome, string reason)
    {
        Outcome = outcome;
        Reason = reason;
    }

    public UccApplyOutcome Outcome { get; }
    public string Reason { get; }

    public uint HouseId { get; init; }
    public ushort HouseTl { get; init; }

    /// <summary>The UCC written to the slot, or zero when it was removed.</summary>
    public ulong UccId { get; init; }
    public uint UccKind { get; init; }
    public uint UccPos { get; init; }

    /// <summary>Slot indexes this request changed.</summary>
    public List<int> ChangedSlots { get; } = [];

    /// <summary>True when the slot table write succeeded; false when it degraded to memory-only state.</summary>
    public bool Persisted { get; set; }

    public uint ConsumedItemId { get; set; }
    public int ConsumedCount { get; set; }

    public bool Success => Outcome == UccApplyOutcome.Applied;
}

/// <summary>
/// The character-scoped half of a UCC apply: finding items the sender owns and paying the
/// configured material through the normal inventory item-task path.
/// </summary>
public interface IUccApplyInventory
{
    /// <summary>Returns an item of the sender's own containers, or null when it is not owned or unknown.</summary>
    Item FindOwnedItem(ulong itemId);

    /// <summary>Returns the sender's own item carrying the given UCC (deterministic lowest id), or null.</summary>
    Item FindOwnedCarrier(ulong uccId);

    /// <summary>Units of the template the sender holds in their inventory.</summary>
    int OwnedCount(uint templateId);

    /// <summary>Consumes units through the item-task path and returns how many were actually consumed.</summary>
    int Consume(uint templateId, int count, Item preferredItem);
}

/// <summary>The world-scoped half of a UCC apply: resolving the target house and writing its slots.</summary>
public interface IUccHousingStore
{
    House FindHouseByTl(ushort tlId);

    /// <summary>Persists the house's UCC slots; returns false when only memory was updated.</summary>
    bool SaveUccSlots(House house);
}

/// <summary>
/// Authorization, material consumption and slot/item mutation for UCC apply requests.
/// Every request is preflighted before anything is consumed: a rejected request changes nothing.
/// </summary>
public sealed class UccApplyService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly UccConfig _config;
    private readonly IUccApplyInventory _inventory;
    private readonly IUccHousingStore _housing;

    public UccApplyService(UccConfig config, IUccApplyInventory inventory, IUccHousingStore housing)
    {
        _config = config;
        _inventory = inventory;
        _housing = housing;
    }

    /// <summary>Builds the runtime service bound to a character's inventory and the housing registry.</summary>
    public static UccApplyService ForCharacter(Character character) =>
        new(AppConfiguration.Instance.Ucc, new CharacterUccApplyInventory(character), new HousingManagerUccStore());

    private static bool IsUsableRow(UccApplyMaterialConfig row) =>
        row is { MaterialItemId: not 0, MaterialCount: >= 0 };

    /// <summary>
    /// Applies the UCC carried by an item the sender owns to every listed target the sender owns.
    /// <paramref name="sourceRef"/> may name the carrier item itself or the UCC it carries; both
    /// readings are resolved against the sender's own containers, so neither can reach a foreign item.
    /// </summary>
    public UccItemApplyResult ApplyToItems(long sourceRef, IReadOnlyList<ulong> targetIds)
    {
        var row = _config?.ItemApply;
        if (!IsUsableRow(row))
        {
            Logger.Error("UCC item apply skipped: no usable Ucc.ItemApply material row is configured");
            return new UccItemApplyResult(UccApplyOutcome.MissingMaterialConfig,
                "Ucc.ItemApply material row is missing");
        }

        if (sourceRef <= 0 || targetIds is null || targetIds.Count == 0)
            return new UccItemApplyResult(UccApplyOutcome.InvalidRequest, "request carries no source or no targets");

        var source = _inventory.FindOwnedItem((ulong)sourceRef);
        if (source is null || source.UccId == 0)
            source = _inventory.FindOwnedCarrier((ulong)sourceRef);
        if (source is null || source.UccId == 0)
        {
            Logger.Warn("UCC item apply rejected: the sender owns no item carrying UCC reference {0}", sourceRef);
            return new UccItemApplyResult(UccApplyOutcome.Unauthorized,
                "no owned item carries the requested UCC");
        }

        var uccId = source.UccId;
        var changed = new List<Item>();
        foreach (var targetId in targetIds)
        {
            if (targetId == 0 || targetId == source.Id)
                return new UccItemApplyResult(UccApplyOutcome.InvalidRequest,
                    "the source item cannot be one of the targets");

            var target = _inventory.FindOwnedItem(targetId);
            if (target is null)
            {
                Logger.Warn("UCC item apply rejected: target item {0} is not one of the sender's items", targetId);
                return new UccItemApplyResult(UccApplyOutcome.Unauthorized,
                    $"target item {targetId} is not owned");
            }

            if (target.UccId == uccId)
                continue; // already carries this UCC; a repeat request must not pay again

            changed.Add(target);
        }

        if (changed.Count == 0)
            return new UccItemApplyResult(UccApplyOutcome.NoChange, "every target already carries the UCC")
            {
                UccId = uccId,
            };

        if (_inventory.OwnedCount(row.MaterialItemId) < row.MaterialCount)
        {
            Logger.Warn("UCC item apply rejected: sender holds less than the configured material {0} x{1}",
                row.MaterialItemId, row.MaterialCount);
            return new UccItemApplyResult(UccApplyOutcome.InsufficientMaterial,
                "configured material is not in the sender's inventory");
        }

        var preferred = source.TemplateId == row.MaterialItemId ? source : null;
        var consumed = _inventory.Consume(row.MaterialItemId, row.MaterialCount, preferred);
        if (consumed <= 0)
        {
            Logger.Warn("UCC item apply aborted: material {0} x{1} could not be consumed, nothing applied",
                row.MaterialItemId, row.MaterialCount);
            return new UccItemApplyResult(UccApplyOutcome.InsufficientMaterial,
                "configured material could not be consumed");
        }

        if (consumed < row.MaterialCount)
            Logger.Error(
                "UCC item apply paid {0} of the configured {1} units of material {2}; continuing with the applied UCC",
                consumed, row.MaterialCount, row.MaterialItemId);

        foreach (var target in changed)
        {
            target.UccId = uccId; // the setter marks the item dirty, so the existing item save persists it
        }

        Logger.Info("UCC item apply: character {0} applied UCC {1} to {2} item(s), paid material {3} x{4}",
            source.OwnerId, uccId, changed.Count, row.MaterialItemId, consumed);

        var result = new UccItemApplyResult(UccApplyOutcome.Applied, null) { UccId = uccId };
        result.ChangedItems.AddRange(changed);
        result.ConsumedItemId = row.MaterialItemId;
        result.ConsumedCount = consumed;
        return result;
    }

    /// <summary>
    /// Applies or removes a UCC on one of a house's five user-content slots. The sender must be the
    /// house's owner or co-owner. Removal consumes nothing; a successful apply pays the configured
    /// housing material exactly once and writes the slot table.
    /// </summary>
    public UccHousingApplyResult ApplyToHousing(Character character, ushort tlId,
        long sourceItemId, sbyte kind, sbyte slotIndex, int pos, bool hasPlacement, bool isRemove)
    {
        var house = _housing.FindHouseByTl(tlId);
        if (house is null)
            return new UccHousingApplyResult(UccApplyOutcome.NotFound, $"no house with target label {tlId}");

        if (character is null || (house.OwnerId != character.Id && house.CoOwnerId != character.Id))
        {
            Logger.Warn("UCC housing apply rejected: character {0} does not own house {1}",
                character?.Id ?? 0u, house.Id);
            return new UccHousingApplyResult(UccApplyOutcome.Unauthorized, "sender does not own the house")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };
        }

        var slots = house.UccSlots;
        var position = (uint)pos;

        if (isRemove)
            return RemoveFromHousing(house, tlId, slotIndex, position, hasPlacement);

        var row = _config?.HousingApply;
        if (!IsUsableRow(row))
        {
            Logger.Error("UCC housing apply skipped: no usable Ucc.HousingApply material row is configured");
            return new UccHousingApplyResult(UccApplyOutcome.MissingMaterialConfig,
                "Ucc.HousingApply material row is missing");
        }

        if (!hasPlacement || slotIndex < 0 || slotIndex >= House.UccSlotCount)
            return new UccHousingApplyResult(UccApplyOutcome.InvalidRequest,
                "housing apply carries no source item or an out-of-range slot")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };

        var source = _inventory.FindOwnedItem((ulong)sourceItemId);
        if (source is null || source.UccId == 0)
        {
            Logger.Warn("UCC housing apply rejected: sender owns no item carrying UCC reference {0}", sourceItemId);
            return new UccHousingApplyResult(UccApplyOutcome.Unauthorized,
                "no owned item carries the requested UCC")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };
        }

        var slot = slots[slotIndex];
        var kindValue = (uint)(byte)kind;
        if (slot.UccId == source.UccId && slot.Kind == kindValue && slot.Position == position)
        {
            return new UccHousingApplyResult(UccApplyOutcome.NoChange, "the slot already carries that UCC")
            {
                HouseId = house.Id,
                HouseTl = tlId,
                UccId = slot.UccId,
                UccKind = slot.Kind,
                UccPos = slot.Position,
            };
        }

        if (_inventory.OwnedCount(row.MaterialItemId) < row.MaterialCount)
        {
            Logger.Warn("UCC housing apply rejected: sender holds less than the configured material {0} x{1}",
                row.MaterialItemId, row.MaterialCount);
            return new UccHousingApplyResult(UccApplyOutcome.InsufficientMaterial,
                "configured material is not in the sender's inventory")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };
        }

        var preferred = source.TemplateId == row.MaterialItemId ? source : null;
        var consumed = _inventory.Consume(row.MaterialItemId, row.MaterialCount, preferred);
        if (consumed <= 0)
        {
            Logger.Warn("UCC housing apply aborted: material {0} x{1} could not be consumed, nothing applied",
                row.MaterialItemId, row.MaterialCount);
            return new UccHousingApplyResult(UccApplyOutcome.InsufficientMaterial,
                "configured material could not be consumed")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };
        }

        if (consumed < row.MaterialCount)
            Logger.Error(
                "UCC housing apply paid {0} of the configured {1} units of material {2}; continuing with the applied UCC",
                consumed, row.MaterialCount, row.MaterialItemId);

        slot.UccId = source.UccId;
        slot.Kind = kindValue;
        slot.Position = position;

        var result = new UccHousingApplyResult(UccApplyOutcome.Applied, null)
        {
            HouseId = house.Id,
            HouseTl = tlId,
            UccId = slot.UccId,
            UccKind = slot.Kind,
            UccPos = slot.Position,
            Persisted = _housing.SaveUccSlots(house),
            ConsumedItemId = row.MaterialItemId,
            ConsumedCount = consumed,
        };
        result.ChangedSlots.Add(slotIndex);

        if (!result.Persisted)
            Logger.Warn("UCC housing apply: house {0} slot {1} updated in memory only", house.Id, slotIndex);
        else
            Logger.Info(
                "UCC housing apply: character {0} applied UCC {1} to house {2} slot {3}, paid material {4} x{5}",
                character.Id, slot.UccId, house.Id, slotIndex, row.MaterialItemId, consumed);

        return result;
    }

    private UccHousingApplyResult RemoveFromHousing(House house, ushort tlId, sbyte slotIndex, uint position,
        bool hasPlacement)
    {
        var slots = house.UccSlots;
        var result = new UccHousingApplyResult(UccApplyOutcome.Applied, null)
        {
            HouseId = house.Id,
            HouseTl = tlId,
        };

        if (hasPlacement)
        {
            if (slotIndex < 0 || slotIndex >= House.UccSlotCount)
                return new UccHousingApplyResult(UccApplyOutcome.InvalidRequest,
                    "housing removal carries an out-of-range slot")
                {
                    HouseId = house.Id,
                    HouseTl = tlId,
                };

            if (!slots[slotIndex].Occupied)
                return new UccHousingApplyResult(UccApplyOutcome.NoChange, "the slot is already empty")
                {
                    HouseId = house.Id,
                    HouseTl = tlId,
                };

            slots[slotIndex].Clear();
            result.ChangedSlots.Add(slotIndex);
        }
        else
        {
            // Without placement fields the request names the position only, so every occupied slot
            // at that position is the removal target.
            for (var i = 0; i < House.UccSlotCount; i++)
            {
                if (!slots[i].Occupied || slots[i].Position != position)
                    continue;
                slots[i].Clear();
                result.ChangedSlots.Add(i);
            }

            if (result.ChangedSlots.Count == 0)
                return new UccHousingApplyResult(UccApplyOutcome.InvalidRequest,
                    $"no applied UCC at position {position}")
                {
                    HouseId = house.Id,
                    HouseTl = tlId,
                };
        }

        result.Persisted = _housing.SaveUccSlots(house);
        Logger.Info("UCC housing removal: house {0} cleared slot(s) {1}",
            house.Id, string.Join(",", result.ChangedSlots));
        return result;
    }
}

/// <summary>Default inventory port backed by a character's own containers.</summary>
public sealed class CharacterUccApplyInventory(Character character) : IUccApplyInventory
{
    public Item FindOwnedItem(ulong itemId) => character.Inventory?.GetItemById(itemId);

    public Item FindOwnedCarrier(ulong uccId)
    {
        if (uccId == 0)
            return null;

        var bag = character.Inventory?.Bag;
        if (bag?.Items is null)
            return null;

        Item best = null;
        foreach (var item in bag.Items)
        {
            if (item is null || item.UccId != uccId)
                continue;
            if (best is null || item.Id < best.Id)
                best = item;
        }

        return best;
    }

    public int OwnedCount(uint templateId)
    {
        if (character.Inventory is null)
            return 0;

        character.Inventory.GetAllItemsByTemplate([SlotType.Inventory], templateId, -1, out _, out var counted);
        return counted;
    }

    public int Consume(uint templateId, int count, Item preferredItem)
    {
        if (count <= 0)
            return 0;

        var bag = character.Inventory?.Bag;
        return bag?.ConsumeItem(ItemTaskType.ImprintUcc, templateId, count, preferredItem) ?? 0;
    }
}

/// <summary>Default housing port backed by the housing registry.</summary>
public sealed class HousingManagerUccStore : IUccHousingStore
{
    public House FindHouseByTl(ushort tlId) => HousingManager.Instance.GetHouseByTlId(tlId);

    public bool SaveUccSlots(House house) => HousingManager.Instance.SaveHouseUccSlots(house);
}
