using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.FishSchools;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

[NotInParallel]
public class FishLootTests
{
    private object _oldManager;
    private object _oldFishData;
    private object _oldItemIds;

    [Before(Test)]
    public void Setup()
    {
        _oldManager = SingletonField<ItemManager>().GetValue(null);
        _oldFishData = SingletonField<FishDetailsGameData>().GetValue(null);
        var itemIdsField = typeof(ItemIdManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _oldItemIds = itemIdsField.GetValue(null);
        var itemIds = new ItemIdManager();
        itemIds.SetUsedIdsLoaderForTest(() => []);
        if (!itemIds.Initialize())
            throw new InvalidOperationException("Failed to initialize isolated test item IDs.");
        itemIdsField.SetValue(null, itemIds);
        var ids = Mock.Of<IItemIdManager>();
        uint nextItemId = 100;
        ids.GetNextId().Returns(() => nextItemId++);
        var manager = new ItemManager(null, ids.Object, null, null, null, null);
        Set(manager, "_templates", new uint[] { 27612, 41523, 41524 }.ToDictionary(
            id => id,
            id => (ItemTemplate)new BackpackTemplate
            {
                Id = id, MaxCount = 1, FixedGrade = 0,
                BackpackType = BackpackType.Fish, BindType = ItemBindType.BindOnPickup
            }));
        Set(manager, "_allItems", new Dictionary<ulong, Item>());
        Set(manager, "_removedItems", new List<ulong>());
        SingletonField<ItemManager>().SetValue(null, manager);
        var templates = (Dictionary<uint, ItemTemplate>)typeof(ItemManager)
            .GetField("_templates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        templates[19000] = new ItemTemplate { Id = 19000, MaxCount = 100, FixedGrade = 0 };
        var fishData = new FishDetailsGameData();
        Set(fishData, "_fishDetails", new Dictionary<uint, FishDetails>
        {
            [27612] = new() { ItemId = 27612, MinLength = 100, MaxLength = 120, MinWeight = 10, MaxWeight = 20 },
            [41523] = new() { ItemId = 41523, MinLength = 225, MaxLength = 282, MinWeight = 405, MaxWeight = 507 },
            [41524] = new() { ItemId = 41524, MinLength = 225, MaxLength = 282, MinWeight = 405, MaxWeight = 507 }
        });
        SingletonField<FishDetailsGameData>().SetValue(null, fishData);
    }

    [After(Test)]
    public void Cleanup()
    {
        SingletonField<ItemManager>().SetValue(null, _oldManager);
        SingletonField<FishDetailsGameData>().SetValue(null, _oldFishData);
        typeof(ItemIdManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, _oldItemIds);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FishEquipsWithFullBagAndRetainsMeasurements(bool lootAll)
    {
        var (player, loot, entry) = CreateCapture();
        await Assert.That(loot.TryTakeLoot(player, 1, null, lootAll)).IsTrue();
        var caught = (BigFish)player.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        await Assert.That(caught.Id).IsEqualTo(100ul);
        await Assert.That(caught.Weight).IsEqualTo(12.75f);
        await Assert.That(caught.Length).IsEqualTo(114.5f);
        await Assert.That(caught.DetailQword).IsEqualTo(123456L);
        await Assert.That(loot.Items).IsEmpty();
        await Assert.That(loot.TryTakeLoot(player, 1, entry, lootAll)).IsFalse();
        await Assert.That(player.Inventory.Equipment.Items).Count().IsEqualTo(1);
        var persisted = new PacketStream();
        caught.WriteDetails(persisted);
        persisted.Pos = 0;
        var restored = new BigFish();
        restored.ReadDetails(persisted);
        await Assert.That(restored.Weight).IsEqualTo(caught.Weight);
        await Assert.That(restored.Length).IsEqualTo(caught.Length);
        await Assert.That(restored.DetailQword).IsEqualTo(caught.DetailQword);
    }

    [Test]
    [Arguments(BackpackType.TradePack)]
    [Arguments(BackpackType.Fish)]
    [Arguments(BackpackType.Glider)]
    public async Task OccupiedBackOrGliderWithFullBagRetainsLoot(BackpackType type)
    {
        var (player, loot, entry) = CreateCapture();
        var equipped = new Backpack(7, new BackpackTemplate { Id = 7, MaxCount = 1, BackpackType = type }, 1)
        { Slot = (int)EquipmentItemSlot.Backpack, SlotType = SlotType.Equipment };
        player.Inventory.Equipment.Items.Add(equipped);
        await Assert.That(loot.TryTakeLoot(player, 1, null, true)).IsFalse();
        await Assert.That(loot.Items[1]).IsSameReferenceAs(entry);
        await Assert.That(player.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack)).IsSameReferenceAs(equipped);
        await Assert.That(ItemManager.Instance.GetItemByItemId(100)).IsNull();
    }

    [Test]
    [Arguments(41523u, false, 0)]
    [Arguments(41523u, true, 0)]
    [Arguments(41524u, false, 0)]
    [Arguments(41524u, true, 0)]
    [Arguments(41523u, false, 3)]
    [Arguments(41523u, true, 3)]
    [Arguments(41524u, false, 3)]
    [Arguments(41524u, true, 3)]
    public async Task FishBundle_IsNotEquippedOrConsumedWhenBagCannotAcceptIt(
        uint templateId, bool lootAll, int bagSize)
    {
        // Retail loots 88732/88733 give three non-stackable packs from NPCs 17370/17371.
        var (player, loot, entry) = CreateCapture(templateId, 3, bagSize);
        var originalId = entry.Item.Id;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.That(loot.TryTakeLoot(player, 1, entry, lootAll)).IsFalse();
            await Assert.That(loot.Items[1]).IsSameReferenceAs(entry);
            await Assert.That(entry.Item.Id).IsEqualTo(originalId);
            await Assert.That(entry.Item.Count).IsEqualTo(3);
            await Assert.That(player.Inventory.Equipment.Items).IsEmpty();
            await Assert.That(player.Inventory.Bag.Items).IsEmpty();
            await Assert.That(ItemManager.Instance.GetItemByItemId(100)).IsNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FishBundle_DoesNotDisplaceExistingGlider(bool lootAll)
    {
        var (player, loot, entry) = CreateCapture(41523, 3, 3);
        var glider = new Backpack(7, new BackpackTemplate
        {
            Id = 7, MaxCount = 1, BackpackType = BackpackType.Glider
        }, 1)
        {
            Slot = (int)EquipmentItemSlot.Backpack, SlotType = SlotType.Equipment,
            _holdingContainer = player.Inventory.Equipment
        };
        player.Inventory.Equipment.Items.Add(glider);
        player.Inventory.PreviousBackPackItemId = 42;

        await Assert.That(loot.TryTakeLoot(player, 1, entry, lootAll)).IsFalse();
        await Assert.That(player.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack)).IsSameReferenceAs(glider);
        await Assert.That(player.Inventory.PreviousBackPackItemId).IsEqualTo(42ul);
        await Assert.That(player.Inventory.Bag.Items).IsEmpty();
        await Assert.That(loot.Items[1]).IsSameReferenceAs(entry);
        await Assert.That(entry.Item.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryLootBundle_StillUsesBagAndCannotBeTakenTwice(bool lootAll)
    {
        var (player, loot, entry) = CreateCapture(19000, 3, 1);

        await Assert.That(loot.TryTakeLoot(player, 1, entry, lootAll)).IsTrue();
        await Assert.That(player.Inventory.Equipment.Items).IsEmpty();
        await Assert.That(player.Inventory.Bag.Items).Count().IsEqualTo(1);
        await Assert.That(player.Inventory.Bag.Items.Single().Count).IsEqualTo(3);
        await Assert.That(player.Inventory.Bag.Items.Single().TemplateId).IsEqualTo(19000u);
        await Assert.That(loot.Items).IsEmpty();
        await Assert.That(loot.TryTakeLoot(player, 1, entry, lootAll)).IsFalse();
        await Assert.That(player.Inventory.Bag.Items.Single().Count).IsEqualTo(3);
    }

    private static (CharacterMock, LootingContainer, LootingContainerItemEntry) CreateCapture(uint templateId = 27612, int count = 1, int bagSize = 0)
    {
        var player = new CharacterMock();
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, player);
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory,
            new ItemContainer(0, SlotType.Inventory, false, null) { ContainerSize = bagSize });
        typeof(Inventory).GetProperty(nameof(Inventory.Equipment))!.SetValue(inventory,
            new EquipmentContainer(0, SlotType.Equipment, false, null));
        player.Inventory = inventory;
        var loot = new LootingContainer(player);
        var template = ItemManager.Instance.GetTemplate(templateId);
        Item generatedItem = template is BackpackTemplate
            ? new BigFish(9000, template, count) { Weight = 12.75f, Length = 114.5f, DetailQword = 123456 }
            : new Item(9000, template, count);
        var entry = new LootingContainerItemEntry { Owner = loot, ItemIndex = 1, Item = generatedItem };
        loot.Items.Add(1, entry);
        return (player, loot, entry);
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
