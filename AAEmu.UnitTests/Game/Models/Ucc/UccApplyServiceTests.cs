using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Ucc;

namespace AAEmu.UnitTests.Game.Models.Ucc;

/// <summary>
/// UCC apply authorization, stamp consumption, persistence and replay. A rejected request must consume
/// nothing; a successful one spends exactly the crest stamp it names, once.
/// </summary>
public sealed class UccApplyServiceTests
{
    private const ulong CarrierUccId = 42;
    private const ulong OtherUccId = 43;
    private const ushort HouseTargetLabel = 500;
    private const int HousePosition = 1234;
    private const uint CrestableTemplate = 900001;
    private const uint UncrestableTemplate = 900002;

    private sealed class FakeInventory(List<Item> items) : IUccApplyInventory
    {
        public List<Item> Items { get; } = items;

        public List<ulong> ConsumedStampIds { get; } = [];

        public bool RefuseConsume { get; init; }

        public Item FindOwnedItem(ulong itemId) => Items.FirstOrDefault(i => i.Id == itemId);

        public Item FindOwnedStamp(ulong uccId) =>
            Items.Where(i => i.UccId == uccId && i.TemplateId == Item.CrestStamp).OrderBy(i => i.Id).FirstOrDefault();

        public bool ConsumeStamp(Item stamp)
        {
            if (RefuseConsume || stamp == null || !Items.Contains(stamp) || stamp.Count <= 0)
                return false;

            ConsumedStampIds.Add(stamp.Id);
            stamp.Count -= 1;

            // An exhausted stack leaves the inventory, like the real container does.
            Items.RemoveAll(i => i.Count <= 0);
            return true;
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

    private static Item Stamp(ulong id = 9100, ulong uccId = CarrierUccId, int count = 1) =>
        new(id, Template(Item.CrestStamp), count) { OwnerId = 10, UccId = uccId };

    private static Item Gear(ulong id, uint template = CrestableTemplate) => new(id, Template(template), 1) { OwnerId = 10 };

    private static bool TakesCrest(uint templateId) => templateId == CrestableTemplate;

    private static UccApplyService Service(FakeInventory inventory, FakeHousingStore housing = null) =>
        new(inventory, housing ?? new FakeHousingStore(), TakesCrest);

    [Test]
    public async Task ItemApply_UnauthorizedTarget_RejectsAndConsumesNothing()
    {
        var source = Stamp();
        var inventory = new FakeInventory([source]);
        var foreignGear = Gear(9200);

        var result = Service(inventory).ApplyToItems((long)source.Id, [foreignGear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumedStampIds).IsEmpty();
        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(foreignGear.UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task ItemApply_Success_SpendsTheSourceStampOnceAndAppliesTheUcc()
    {
        var source = Stamp();
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, gear]);

        var result = Service(inventory).ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(result.UccId).IsEqualTo(CarrierUccId);
        await Assert.That(gear.UccId).IsEqualTo(CarrierUccId);
        await Assert.That(gear.IsDirty).IsTrue();
        await Assert.That(inventory.ConsumedStampIds).IsEquivalentTo(new[] { source.Id });
        await Assert.That(result.ConsumedItemId).IsEqualTo(Item.CrestStamp);
        await Assert.That(result.ConsumedCount).IsEqualTo(1);
        await Assert.That(result.ChangedItems.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemApply_WithAnotherCrestsStampInTheBag_SpendsOnlyTheNamedStamp()
    {
        // The other stamp is the lower id, so a take-any-of-the-template consume would pick it first.
        var otherCrest = Stamp(9050, OtherUccId);
        var source = Stamp(9100, CarrierUccId);
        var gear = Gear(9300);
        var inventory = new FakeInventory([otherCrest, source, gear]);

        var result = Service(inventory).ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(inventory.ConsumedStampIds).IsEquivalentTo(new[] { source.Id });
        await Assert.That(otherCrest.Count).IsEqualTo(1);
        await Assert.That(gear.UccId).IsEqualTo(CarrierUccId);
    }

    [Test]
    public async Task ItemApply_SourceThatIsNotAStamp_IsRefusedAndConsumesNothing()
    {
        // A crested cloak carries the UCC too, but it is not what prints it.
        var crestedCloak = new Item(9100, Template(CrestableTemplate), 1) { OwnerId = 10, UccId = CarrierUccId };
        var otherCrest = Stamp(9050, OtherUccId);
        var gear = Gear(9300);
        var inventory = new FakeInventory([crestedCloak, otherCrest, gear]);

        var result = Service(inventory).ApplyToItems((long)crestedCloak.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumedStampIds).IsEmpty();
        await Assert.That(crestedCloak.Count).IsEqualTo(1);
        await Assert.That(otherCrest.Count).IsEqualTo(1);
        await Assert.That(gear.UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task ItemApply_SourceNamedByItsUcc_ResolvesTheStampNotACrestedPiece()
    {
        var crestedCloak = new Item(9001, Template(CrestableTemplate), 1) { OwnerId = 10, UccId = CarrierUccId };
        var source = Stamp(9100, CarrierUccId);
        var gear = Gear(9300);
        var inventory = new FakeInventory([crestedCloak, source, gear]);

        var result = Service(inventory).ApplyToItems((long)CarrierUccId, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(inventory.ConsumedStampIds).IsEquivalentTo(new[] { source.Id });
        await Assert.That(crestedCloak.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ItemApply_TargetThatCannotCarryACrest_IsRefusedAndConsumesNothing()
    {
        var source = Stamp();
        var weapon = Gear(9300, UncrestableTemplate);
        var inventory = new FakeInventory([source, weapon]);

        var result = Service(inventory).ApplyToItems((long)source.Id, [weapon.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.NotApplicable);
        await Assert.That(inventory.ConsumedStampIds).IsEmpty();
        await Assert.That(weapon.UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task ItemApply_SecondIdenticalRequest_ChangesNothingAndConsumesNothingMore()
    {
        var source = Stamp(count: 2);
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, gear]);
        var service = Service(inventory);

        var first = service.ApplyToItems((long)source.Id, [gear.Id]);
        var second = service.ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(first.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(second.Outcome).IsEqualTo(UccApplyOutcome.NoChange);
        await Assert.That(inventory.ConsumedStampIds.Count).IsEqualTo(1);
        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(gear.UccId).IsEqualTo(CarrierUccId);
    }

    [Test]
    public async Task ItemApply_StampThatCannotBeConsumed_AppliesNothing()
    {
        var source = Stamp();
        var gear = Gear(9300);
        var inventory = new FakeInventory([source, gear]) { RefuseConsume = true };

        var result = Service(inventory).ApplyToItems((long)source.Id, [gear.Id]);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.InsufficientMaterial);
        await Assert.That(gear.UccId).IsEqualTo(0ul);
        await Assert.That(source.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HousingApply_NonOwner_IsRejectedAndConsumesNothing()
    {
        var stranger = new Character(new UnitCustomModelParams()) { Id = 2 };
        var source = Stamp();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };

        var result = Service(inventory, store).ApplyToHousing(stranger, HouseTargetLabel, (long)source.Id, 1, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumedStampIds).IsEmpty();
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(0);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task HousingApply_Owner_PersistsTheSlotAndItReplaysAfterReload()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Stamp();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };

        var result = Service(inventory, store).ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(result.Persisted).IsTrue();
        await Assert.That(result.ConsumedCount).IsEqualTo(1);
        await Assert.That(inventory.ConsumedStampIds).IsEquivalentTo(new[] { source.Id });
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
    public async Task HousingApply_WithAnotherCrestsStampInTheBag_SpendsOnlyTheNamedStamp()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var otherCrest = Stamp(9050, OtherUccId);
        var source = Stamp(9100, CarrierUccId);
        var inventory = new FakeInventory([otherCrest, source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };

        var result = Service(inventory, store).ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(inventory.ConsumedStampIds).IsEquivalentTo(new[] { source.Id });
        await Assert.That(otherCrest.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HousingApply_SourceThatIsNotAStamp_IsRefusedAndConsumesNothing()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var crestedCloak = new Item(9100, Template(CrestableTemplate), 1) { OwnerId = 10, UccId = CarrierUccId };
        var inventory = new FakeInventory([crestedCloak]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };

        var result = Service(inventory, store).ApplyToHousing(owner, HouseTargetLabel, (long)crestedCloak.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(result.Outcome).IsEqualTo(UccApplyOutcome.InvalidRequest);
        await Assert.That(inventory.ConsumedStampIds).IsEmpty();
        await Assert.That(crestedCloak.Count).IsEqualTo(1);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(0);
    }

    [Test]
    public async Task HousingApply_SecondIdenticalRequest_ConsumesNothingMore()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Stamp();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = Service(inventory, store);

        var first = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);
        var second = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);

        await Assert.That(first.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        // The first apply spent the only stamp, so the repeat request has no owned source left.
        await Assert.That(second.Outcome).IsEqualTo(UccApplyOutcome.Unauthorized);
        await Assert.That(inventory.ConsumedStampIds.Count).IsEqualTo(1);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HousingRemove_ClearsTheSlotWithoutConsumingAnything()
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 1 };
        var source = Stamp();
        var inventory = new FakeInventory([source]);
        var store = new FakeHousingStore();
        store.Houses[HouseTargetLabel] = new House { Id = 7, OwnerId = 1 };
        var service = Service(inventory, store);

        _ = service.ApplyToHousing(owner, HouseTargetLabel, (long)source.Id, 3, 2,
            HousePosition, hasPlacement: true, isRemove: false);
        var consumesAfterApply = inventory.ConsumedStampIds.Count;

        // A removal carries no source item, so it names the position it clears.
        var removal = service.ApplyToHousing(owner, HouseTargetLabel, 0, 0, 0,
            HousePosition, hasPlacement: false, isRemove: true);

        await Assert.That(removal.Outcome).IsEqualTo(UccApplyOutcome.Applied);
        await Assert.That(store.Houses[HouseTargetLabel].UccSlots[2].UccId).IsEqualTo(0ul);
        await Assert.That(inventory.ConsumedStampIds.Count).IsEqualTo(consumesAfterApply);
        await Assert.That(store.SavedHouseIds.Count).IsEqualTo(2);

        var reloaded = store.ReloadHouse(HouseTargetLabel);
        await Assert.That(reloaded.UccSlots[2].UccId).IsEqualTo(0ul);
    }

    [Test]
    public async Task HousingUpdateRecipients_ReachesEveryViewerAndTheApplierOnce()
    {
        var applier = new Character(new UnitCustomModelParams()) { Id = 1 };
        var neighbour = new Character(new UnitCustomModelParams()) { Id = 2 };

        var inView = UccApplyService.HousingUpdateRecipients(applier, [applier, neighbour, neighbour]);
        await Assert.That(inView.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(inView[0], applier)).IsTrue();
        await Assert.That(ReferenceEquals(inView[1], neighbour)).IsTrue();

        // An applier outside the house's view still hears the result of their own request.
        var outOfView = UccApplyService.HousingUpdateRecipients(applier, [neighbour]);
        await Assert.That(outOfView.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(outOfView[0], neighbour)).IsTrue();
        await Assert.That(ReferenceEquals(outOfView[1], applier)).IsTrue();
    }
}
