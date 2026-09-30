using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

/// <summary>
/// A stack that <see cref="ItemContainer.AddOrMoveExistingItem"/> merges into an existing one is gone
/// as an item. It has to be released like the merged-away stack of an inventory merge, or it is saved
/// as an orphan row with its old container and slot.
/// </summary>
[NotInParallel]
public sealed class ItemContainerMergeReleaseTests : IDisposable
{
    // Item 31892 from the housing test: 20 and 10 arrived by mail onto a bag stack.
    private static readonly ItemTemplate Stackable = new() { Id = 31892, MaxCount = 1000, BindType = ItemBindType.Normal };

    private readonly Dictionary<ulong, Item> _allItems = [];
    private readonly List<ulong> _removedItems = [];
    private readonly SingletonScope<ItemManager> _items;

    public ItemContainerMergeReleaseTests()
    {
        var items = new ItemManager(
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object);
        SetField(items, "_allItems", _allItems);
        SetField(items, "_removedItems", _removedItems);
        _items = new SingletonScope<ItemManager>(items);
    }

    public void Dispose() => _items.Dispose();

    [Test]
    public async Task MailAttachmentTakenOntoABagStack_IsReleased()
    {
        var bag = new ItemContainer(0, SlotType.Inventory, false, null);
        var mail = new ItemContainer(0, SlotType.Mail, false, null);
        var stack = Register(new ItemMock(16777440, Stackable, 5));
        var attachment = Register(new ItemMock(16777443, Stackable, 20));
        bag.AddOrMoveExistingItem(ItemTaskType.Invalid, stack, 0);
        mail.AddOrMoveExistingItem(ItemTaskType.Invalid, attachment, 0);

        // CharacterMails.GetAttached passes the matching bag stack's slot.
        var moved = bag.AddOrMoveExistingItem(ItemTaskType.Mail, attachment, stack.Slot);

        await Assert.That(moved).IsTrue();
        await Assert.That(stack.Count).IsEqualTo(25);
        await Assert.That(mail.Items).IsEmpty();
        await Assert.That(_allItems.ContainsKey(attachment.Id)).IsFalse();
        await Assert.That(_removedItems).Contains(attachment.Id);
    }

    [Test]
    public async Task NewItemMergedIntoAStack_IsReleased()
    {
        var bag = new ItemContainer(0, SlotType.Inventory, false, null);
        var stack = Register(new ItemMock(16777440, Stackable, 5));
        var incoming = Register(new ItemMock(16777451, Stackable, 10));
        bag.AddOrMoveExistingItem(ItemTaskType.Invalid, stack, 0);

        bag.AddOrMoveExistingItem(ItemTaskType.Invalid, incoming, stack.Slot);

        await Assert.That(stack.Count).IsEqualTo(15);
        await Assert.That(_removedItems).Contains(incoming.Id);
    }

    [Test]
    public async Task ItemMovedIntoAnEmptySlot_IsKept()
    {
        var bag = new ItemContainer(0, SlotType.Inventory, false, null);
        var mail = new ItemContainer(0, SlotType.Mail, false, null);
        var attachment = Register(new ItemMock(16777443, Stackable, 20));
        mail.AddOrMoveExistingItem(ItemTaskType.Invalid, attachment, 0);

        bag.AddOrMoveExistingItem(ItemTaskType.Mail, attachment);

        await Assert.That(bag.Items).Contains(attachment);
        await Assert.That(attachment.SlotType).IsEqualTo(SlotType.Inventory);
        await Assert.That(_allItems.ContainsKey(attachment.Id)).IsTrue();
        await Assert.That(_removedItems).IsEmpty();
    }

    private Item Register(Item item)
    {
        _allItems[item.Id] = item;
        return item;
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
}
