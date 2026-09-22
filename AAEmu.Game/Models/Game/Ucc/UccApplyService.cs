using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
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

    /// <summary>The sender does not own the source, the target, or the house.</summary>
    Unauthorized,

    /// <summary>A referenced object does not exist.</summary>
    NotFound,

    /// <summary>The request body is malformed, carries no usable placement, or its source is not a crest stamp.</summary>
    InvalidRequest,

    /// <summary>A target item is not one of the templates that can carry a crest; nothing was consumed.</summary>
    NotApplicable,

    /// <summary>The crest stamp could not be consumed; nothing was applied.</summary>
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
/// The character-scoped half of a UCC apply: finding items the sender owns and spending the crest stamp
/// through the normal inventory item-task path.
/// </summary>
public interface IUccApplyInventory
{
    /// <summary>Returns an item of the sender's own containers, or null when it is not owned or unknown.</summary>
    Item FindOwnedItem(ulong itemId);

    /// <summary>Returns the sender's own crest stamp carrying the given UCC (deterministic lowest id), or null.</summary>
    Item FindOwnedStamp(ulong uccId);

    /// <summary>Consumes one unit of that exact stamp through the item-task path; false when it could not.</summary>
    bool ConsumeStamp(Item stamp);
}

/// <summary>The world-scoped half of a UCC apply: resolving the target house and writing its slots.</summary>
public interface IUccHousingStore
{
    House FindHouseByTl(ushort tlId);

    /// <summary>Persists the house's UCC slots; returns false when only memory was updated.</summary>
    bool SaveUccSlots(House house);
}

/// <summary>
/// Authorization, stamp consumption and slot/item mutation for UCC apply requests.
/// Every request is preflighted before anything is consumed: a rejected request changes nothing.
/// </summary>
/// <remarks>
/// A crest is applied with a crest stamp, and every stamp carries the UCC of the crest it prints. The apply
/// therefore spends the stamp the request names, never another item of the same template, which would be a
/// stamp for a different crest.
/// </remarks>
public sealed class UccApplyService
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly IUccApplyInventory _inventory;
    private readonly IUccHousingStore _housing;
    private readonly Func<uint, bool> _takesCrest;

    /// <param name="takesCrest">Whether an item template can carry a crest (the shipped applicable list).</param>
    public UccApplyService(IUccApplyInventory inventory, IUccHousingStore housing, Func<uint, bool> takesCrest)
    {
        _inventory = inventory;
        _housing = housing;
        _takesCrest = takesCrest;
    }

    /// <summary>Builds the runtime service bound to a character's inventory and the housing registry.</summary>
    public static UccApplyService ForCharacter(Character character) =>
        new(new CharacterUccApplyInventory(character), new HousingManagerUccStore(), UccGameData.Instance.TakesCrest);

    private static bool IsStamp(Item item) => item is { UccId: not 0 } && item.TemplateId == Item.CrestStamp;

    /// <summary>
    /// Everyone a house crest change has to reach: the players who have the house loaded, and the applier
    /// even when the house is outside their view, each once.
    /// </summary>
    public static List<Character> HousingUpdateRecipients(Character applier, IEnumerable<Character> viewers)
    {
        var recipients = new List<Character>();
        foreach (var viewer in viewers ?? [])
        {
            if (viewer != null && !recipients.Any(r => ReferenceEquals(r, viewer)))
                recipients.Add(viewer);
        }

        if (applier != null && !recipients.Any(r => ReferenceEquals(r, applier)))
            recipients.Add(applier);
        return recipients;
    }

    /// <summary>
    /// Applies the UCC printed by a crest stamp the sender owns to every listed target the sender owns.
    /// <paramref name="sourceRef"/> may name the stamp itself or the UCC it carries; both readings are
    /// resolved against the sender's own stamps, so neither can reach a foreign item or a crested piece.
    /// </summary>
    public UccItemApplyResult ApplyToItems(long sourceRef, IReadOnlyList<ulong> targetIds)
    {
        if (sourceRef <= 0 || targetIds is null || targetIds.Count == 0)
            return new UccItemApplyResult(UccApplyOutcome.InvalidRequest, "request carries no source or no targets");

        var source = _inventory.FindOwnedItem((ulong)sourceRef);
        if (!IsStamp(source))
            source = _inventory.FindOwnedStamp((ulong)sourceRef);
        if (!IsStamp(source))
        {
            Logger.Warn("UCC item apply rejected: the sender owns no crest stamp for reference {0}", sourceRef);
            return new UccItemApplyResult(UccApplyOutcome.Unauthorized,
                "no owned crest stamp carries the requested UCC");
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

            if (_takesCrest == null || !_takesCrest(target.TemplateId))
            {
                Logger.Warn("UCC item apply rejected: target item {0} (template {1}) cannot carry a crest",
                    targetId, target.TemplateId);
                return new UccItemApplyResult(UccApplyOutcome.NotApplicable,
                    $"target item {targetId} cannot carry a crest");
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

        var stampTemplate = source.TemplateId;
        if (!_inventory.ConsumeStamp(source))
        {
            Logger.Warn("UCC item apply aborted: crest stamp {0} could not be consumed, nothing applied", source.Id);
            return new UccItemApplyResult(UccApplyOutcome.InsufficientMaterial,
                "the crest stamp could not be consumed");
        }

        foreach (var target in changed)
        {
            target.UccId = uccId; // the setter marks the item dirty, so the existing item save persists it
        }

        Logger.Info("UCC item apply: character {0} applied UCC {1} to {2} item(s), spent crest stamp {3}",
            source.OwnerId, uccId, changed.Count, source.Id);

        var result = new UccItemApplyResult(UccApplyOutcome.Applied, null) { UccId = uccId };
        result.ChangedItems.AddRange(changed);
        result.ConsumedItemId = stampTemplate;
        result.ConsumedCount = 1;
        return result;
    }

    /// <summary>
    /// Applies or removes a UCC on one of a house's five user-content slots. The sender must be the
    /// house's owner or co-owner. Removal consumes nothing; a successful apply spends the crest stamp it
    /// names and writes the slot table.
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

        if (!IsStamp(source))
        {
            Logger.Warn("UCC housing apply rejected: source item {0} (template {1}) is not a crest stamp",
                source.Id, source.TemplateId);
            return new UccHousingApplyResult(UccApplyOutcome.InvalidRequest, "the source is not a crest stamp")
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

        var uccId = source.UccId;
        var stampTemplate = source.TemplateId;
        if (!_inventory.ConsumeStamp(source))
        {
            Logger.Warn("UCC housing apply aborted: crest stamp {0} could not be consumed, nothing applied", source.Id);
            return new UccHousingApplyResult(UccApplyOutcome.InsufficientMaterial,
                "the crest stamp could not be consumed")
            {
                HouseId = house.Id,
                HouseTl = tlId,
            };
        }

        slot.UccId = uccId;
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
            ConsumedItemId = stampTemplate,
            ConsumedCount = 1,
        };
        result.ChangedSlots.Add(slotIndex);

        if (!result.Persisted)
            Logger.Warn("UCC housing apply: house {0} slot {1} updated in memory only", house.Id, slotIndex);
        else
            Logger.Info(
                "UCC housing apply: character {0} applied UCC {1} to house {2} slot {3}, spent crest stamp {4}",
                character.Id, slot.UccId, house.Id, slotIndex, source.Id);

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

    public Item FindOwnedStamp(ulong uccId)
    {
        if (uccId == 0)
            return null;

        var bag = character.Inventory?.Bag;
        if (bag?.Items is null)
            return null;

        Item best = null;
        foreach (var item in bag.Items)
        {
            if (item is null || item.UccId != uccId || item.TemplateId != Item.CrestStamp)
                continue;
            if (best is null || item.Id < best.Id)
                best = item;
        }

        return best;
    }

    public bool ConsumeStamp(Item stamp)
    {
        var bag = character.Inventory?.Bag;
        if (bag == null || stamp == null || !ReferenceEquals(stamp._holdingContainer, bag))
            return false;

        // The stamp is the preferred item, so the one unit comes out of exactly that stamp.
        return bag.ConsumeItem(ItemTaskType.ImprintUcc, stamp.TemplateId, 1, stamp) == 1;
    }
}

/// <summary>Default housing port backed by the housing registry.</summary>
public sealed class HousingManagerUccStore : IUccHousingStore
{
    public House FindHouseByTl(ushort tlId) => HousingManager.Instance.GetHouseByTlId(tlId);

    public bool SaveUccSlots(House house) => HousingManager.Instance.SaveHouseUccSlots(house);
}
