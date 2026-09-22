using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Ucc;

namespace AAEmu.UnitTests.Game.Models.Ucc;

/// <summary>
/// UCC apply authorization, material consumption, persistence and replay. A rejected request must
/// consume nothing; a successful one pays exactly the configured material once.
/// </summary>
public sealed class UccApplyServiceTests
{
    private const ulong CarrierUccId = 42;
    private const ushort HouseTargetLabel = 500;
    private const int HousePosition = 1234;

    private sealed class FakeInventory(List<Item> items) : IUccApplyInventory
    {
        public List<Item> Items { get; } = items;

        public List<(uint TemplateId, int Count, ulong PreferredId)> ConsumeCalls { get; } = [];

        public Item FindOwnedItem(ulong itemId) => Items.FirstOrDefault(i => i.Id == itemId);

        public Item FindOwnedCarrier(ulong uccId) =>
            Items.Where(i => i.UccId == uccId).OrderBy(i => i.Id).FirstOrDefault();

        public int OwnedCount(uint templateId) =>
            Items.Where(i => i.TemplateId == templateId).Sum(i => i.Count);

        public int Consume(uint templateId, int count, Item preferredItem)
        {
            ConsumeCalls.Add((templateId, count, preferredItem?.Id ?? 0));
            if (count <= 0)
                return 0;

            var remaining = count;
            var candidates = Items
                .Where(i => i.TemplateId == templateId)
                .OrderByDescending(i => preferredItem != null && i.Id == preferredItem.Id)
                .ToList();

            foreach (var item in candidates)
            {
                if (remaining == 0)
                    break;
                var take = Math.Min(item.Count, remaining);
                item.Count -= take;
                remaining -= take;
            }

            // An exhausted stack leaves the inventory, like the real container does.
            Items.RemoveAll(i => i.Count <= 0);
            return count - remaining;
        }
    }

    private sealed class FakeHousingStore : IUccHousingStore
    {
        public Dictionary<ushort, House> Houses { get; } = [];
        public List<uint> SavedHouseIds { get; } = [];
        private readonly Dictionary<uint, HouseUccSlot[]> _snapshots = [];

        public House FindHouseByTl(ushort tlId) => Houses.GetValueOrDefault(tlId);

        public bool SaveUccSlots(House house)
        {
            SavedHouseIds.Add(house.Id);
            _snapshots[house.Id] = house.UccSlots
                .Select(s => new HouseUccSlot { UccId = s.UccId, Kind = s.Kind, Position = s.Position })
                .ToArray();
            return true;
        }

        /// <summary>Simulates a relog: a brand-new house object filled from the stored snapshot.</summary>
        public House ReloadHouse(ushort tlId)
        {
            var stored = Houses[tlId];
            var fresh = new House { Id = stored.Id, OwnerId = stored.OwnerId, CoOwnerId = stored.CoOwnerId };
            if (_snapshots.TryGetValue(stored.Id, out var snapshot))
            {
                for (var i = 0; i < snapshot.Length; i++)
                {
                    fresh.UccSlots[i].UccId = snapshot[i].UccId;
                    fresh.UccSlots[i].Kind = snapshot[i].Kind;
                    fresh.UccSlots[i].Position = snapshot[i].Position;
                }
            }

            Houses[tlId] = fresh;
            return fresh;
        }
    }

    private static ItemTemplate Template(uint id) => new() { Id = id };

    private static Item Carrier() =>
        new(9100, Template(Item.CrestStamp), 1) { OwnerId = 10, UccId = CarrierUccId };

    private static Item Gear(ulong id) => new(id, Template(900001), 1) { OwnerId = 10 };

    private static UccConfig ConfigWithStampMaterial() => new()
    {
        ItemApply = new UccApplyMaterialConfig { MaterialItemId = Item.CrestStamp, MaterialCount = 1 },
        HousingApply = new UccApplyMaterialConfig { MaterialItemId = Item.CrestStamp, MaterialCount = 1 },
    };

    [Test]
    public async Task ItemApply_UnauthorizedTarget_RejectsAndConsumesNothing()
    {
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var service = new UccApplyService(ConfigWithStampMaterial(), inventory, new FakeHousingStore());
        var foreignGear = Gear(9200);

        var result = service.ApplyToItems((long)source.Id, [foreignGear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(0);
        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(source.UccId).IsEqualTo(CarrierUccId);
        await Assert.That(foreignGear.UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task ItemApply_Success_ConsumesConfiguredMaterialOnceAndAppliesTheUcc()
    {
        var source = Carrier();
        var materialStack = new Item(9150, Template(Item.CrestInk), 3) { OwnerId = 10 };
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, materialStack, gear]);
        var config = new UccConfig
        {
            ItemApply = new UccApplyMaterialConfig { MaterialItemId = Item.CrestInk, MaterialCount = 1 },
        };
        var service = new UccApplyService(config, inventory, new FakeHousingStore());

        var result = service.ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(result.UccId).IsEqualTo(CarrierUccId);
        await Assert.That(gear.UccId).IsEqualTo(CarrierUccId);
        await Assert.That(gear.IsDirty).IsTrue();
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(1);
        await Assert.That(inventory.ConsumeCalls[0].TemplateId).IsEqualTo(Item.CrestInk);
        await Assert.That(inventory.ConsumeCalls[0].Count).IsEqualTo(1);
        await Assert.That(materialStack.Count).IsEqualTo(2);
        await Assert.That(result.ChangedItems.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemApply_SecondIdenticalRequest_ChangesNothingAndConsumesNothingMore()
    {
        var source = Carrier();
        var materialStack = new Item(9150, Template(Item.CrestInk), 3) { OwnerId = 10 };
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, materialStack, gear]);
        var config = new UccConfig
        {
            ItemApply = new UccApplyMaterialConfig { MaterialItemId = Item.CrestInk, MaterialCount = 1 },
        };
        var service = new UccApplyService(config, inventory, new FakeHousingStore());

        var first = service.ApplyToItems((long)source.Id, [gear.Id]);
        var second = service.ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(first.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(second.Outcome).IsEqualTo(UccApplyOutcome.NoChange);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(1);
        await Assert.That(materialStack.Count).IsEqualTo(2);
        await Assert.That(gear.UccId).IsEqualTo(CarrierUccId);
    }

    [Test]
    public async Task ItemApply_MissingMaterialRow_SkipsWithoutConsumingOrChangingAnything()
    {
        var source = Carrier();
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, gear]);
        var service = new UccApplyService(new UccConfig(), inventory, new FakeHousingStore());

        var result = service.ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.MissingMaterialConfig);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(0);
        await Assert.That(gear.UccId).IsEqualTo(0ul);
        await Assert.That(source.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemApply_InsufficientMaterial_FailsWithoutConsumingOrChangingAnything()
    {
        var source = Carrier();
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, gear]); // no configured material in the bag
        var config = new UccConfig
        {
            ItemApply = new UccApplyMaterialConfig { MaterialItemId = Item.CrestInk, MaterialCount = 1 },
        };
        var service = new UccApplyService(config, inventory, new FakeHousingStore());

        var result = service.ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.InsufficientMaterial);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(0);
        await Assert.That(gear.UccId).IsEqualTo(0ul);
        await Assert.That(source.UccId).IsEqualTo(CarrierUccId);
    }

    [Test]
    public async Task HousingApply_NonOwner_IsRejectedAndConsumesNothing()
    {
        var stranger = new Character(new UnitCustomModelParams()) { Id = 2 };
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = new UccApplyService(ConfigWithStampMaterial(), inventory, store);

        var result = service.ApplyToHousing(stranger, HouseTargetLabel, (long)source.Id, 1, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(0);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(0);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task HousingApply_Owner_PersistsTheSlotAndItReplaysAfterReload()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = new UccApplyService(ConfigWithStampMaterial(), inventory, store);

        var result = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(result.Persisted).IsTrue();
        await Assert.That(result.ConsumedCount).IsEqualTo(1);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(1);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(CarrierUccId);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].Kind).IsEqualTo(3u);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].Position)
            .IsEqualTo((uint)HousePosition);

        // Relog replay: a fresh house object is filled from what was persisted.
        var reloaded = store.ReloadHouse(HouseTargetLabel);
        await Assert.That(reloaded.UccSlots[2].UccId).IsEqualTo(CarrierUccId);
        await Assert.That(reloaded.UccSlots[2].Kind).IsEqualTo(3u);
        await Assert.That(reloaded.UccSlots[2].Position).IsEqualTo((uint)HousePosition);
        await Assert.That(reloaded.UccSlots[0].UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task HousingApply_SecondIdenticalRequest_ConsumesNothingMore()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = new UccApplyService(ConfigWithStampMaterial(), inventory, store);

        var first = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);
        var second = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(first.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        // The first apply paid the configured material (the carrier itself), so the repeat request
        // has no owned source left and cannot pay again.
        await Assert.That(second.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(1);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HousingRemove_ClearsTheSlotWithoutConsumingAnything()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = new UccApplyService(ConfigWithStampMaterial(), inventory, store);

        _ = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);
        var consumesAfterApply = inventory.ConsumeCalls.Count;

        // A removal carries no source item, so it names the position it clears.
        var removal = service.ApplyToHousing(owner, HouseTargetLabel, 0, 0, 0,
            HousePosition, hasPlacement: false, isRemove: true);

        await Assert.That(removal.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(0ul);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(consumesAfterApply);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(2);

        var reloaded = store.ReloadHouse(HouseTargetLabel);
        await Assert.That(reloaded.UccSlots[2].UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task HousingApply_MissingMaterialRow_SkipsWithoutConsumingOrSaving()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Carrier();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = new UccApplyService(new UccConfig(), inventory, store);

        var result = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.MissingMaterialConfig);
        await Assert.That(inventory.ConsumeCalls.Count).IsEqualTo(0);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(0);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(0ul);
    }
}
