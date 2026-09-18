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

    [Before(Test)]
    public void Setup()
    {
        _oldManager = SingletonField<ItemManager>().GetValue(null);
        _oldFishData = SingletonField<FishDetailsGameData>().GetValue(null);
        var ids = Mock.Of<IItemIdManager>();
        ids.GetNextId().Returns(100u);
        var manager = new ItemManager(null, ids.Object, null, null, null, null);
        Set(manager, "_templates", new Dictionary<uint, ItemTemplate>
        {
            [27612] = new BackpackTemplate
            {
                Id = 27612, MaxCount = 1, FixedGrade = 0,
                BackpackType = BackpackType.Fish, BindType = ItemBindType.BindOnPickup
            }
        });
        Set(manager, "_allItems", new Dictionary<ulong, Item>());
        Set(manager, "_removedItems", new List<ulong>());
        SingletonField<ItemManager>().SetValue(null, manager);
        var fishData = new FishDetailsGameData();
        Set(fishData, "_fishDetails", new Dictionary<uint, FishDetails>
        {
            [27612] = new() { ItemId = 27612, MinLength = 100, MaxLength = 120, MinWeight = 10, MaxWeight = 20 }
        });
        SingletonField<FishDetailsGameData>().SetValue(null, fishData);
    }

    [After(Test)]
    public void Cleanup()
    {
        SingletonField<ItemManager>().SetValue(null, _oldManager);
        SingletonField<FishDetailsGameData>().SetValue(null, _oldFishData);
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

    private static (CharacterMock, LootingContainer, LootingContainerItemEntry) CreateCapture()
    {
        var player = new CharacterMock();
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, player);
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory,
            new ItemContainer(0, SlotType.Inventory, false, null) { ContainerSize = 0 });
        typeof(Inventory).GetProperty(nameof(Inventory.Equipment))!.SetValue(inventory,
            new EquipmentContainer(0, SlotType.Equipment, false, null));
        player.Inventory = inventory;
        var loot = new LootingContainer(player);
        var fish = new BigFish(9000, ItemManager.Instance.GetTemplate(27612), 1)
        { Weight = 12.75f, Length = 114.5f, DetailQword = 123456 };
        var entry = new LootingContainerItemEntry { Owner = loot, ItemIndex = 1, Item = fish };
        loot.Items.Add(1, entry);
        return (player, loot, entry);
    }

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
