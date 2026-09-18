using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Containers;

[NotInParallel]
public class ItemStackAcquisitionTests
{
    private static readonly FieldInfo InstanceField = typeof(Singleton<ItemManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly ItemTemplate _template = new() { Id = 19000, MaxCount = 1000, FixedGrade = 0 };
    private object _previousManager;

    [Before(Test)]
    public void InstallItemManager()
    {
        _previousManager = InstanceField.GetValue(null);
        var manager = new ItemManager(null, null, null, null, null, null);
        typeof(ItemManager).GetField("_templates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, new Dictionary<uint, ItemTemplate> { [_template.Id] = _template });
        InstanceField.SetValue(null, manager);
    }

    [After(Test)]
    public void RestoreItemManager() => InstanceField.SetValue(null, _previousManager);

    [Test]
    [Arguments(100, 600, 500, 1)]
    [Arguments(500, 1000, 500, 1)]
    [Arguments(600, 1000, 600, 2)]
    [Arguments(1000, 1000, 1000, 2)]
    public async Task Acquire_StopsAfterRequestedAmountWithoutTouchingLaterStacks(
        int amount, int firstCount, int secondCount, int updatedCount)
    {
        var bag = CreateFullBag();
        var bySlot = bag.Items.OrderBy(item => item.Slot).ToArray();

        var acquired = bag.AcquireDefaultItemEx(ItemTaskType.Loot, _template.Id, amount, 0,
            out var created, out var updated, 0);

        await Assert.That(acquired).IsTrue();
        await Assert.That(created).IsEmpty();
        await Assert.That(updated.Count).IsEqualTo(updatedCount);
        await Assert.That(updated.SequenceEqual(bySlot.Take(updatedCount))).IsTrue();
        await Assert.That(bySlot[0].Count).IsEqualTo(firstCount);
        await Assert.That(bySlot[1].Count).IsEqualTo(secondCount);
        await Assert.That(bySlot[2].Count).IsEqualTo(500);
        await Assert.That(bag.Items.Sum(item => item.Count)).IsEqualTo(1500 + amount);
        await Assert.That(bag.Items.Count).IsEqualTo(3);
        await Assert.That(bag.FreeSlotCount).IsEqualTo(0);
        foreach (var untouched in bySlot.Skip(updatedCount))
            await Assert.That(untouched.IsDirty).IsFalse();
    }

    [Test]
    public async Task Acquire_TwoStacksOf500_Adding100CompletesWith600And500()
    {
        var bag = CreateFullBag();
        bag.Items.RemoveAll(item => item.Slot == 2);
        bag.ContainerSize = 2;

        var acquired = bag.AcquireDefaultItemEx(ItemTaskType.Loot, _template.Id, 100, 0,
            out var created, out var updated, 0);

        await Assert.That(acquired).IsTrue();
        await Assert.That(created).IsEmpty();
        await Assert.That(updated.Count).IsEqualTo(1);
        await Assert.That(bag.Items.Single(item => item.Slot == 0).Count).IsEqualTo(600);
        await Assert.That(bag.Items.Single(item => item.Slot == 1).Count).IsEqualTo(500);
        await Assert.That(bag.Items.Sum(item => item.Count)).IsEqualTo(1100);
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(-1, true)]
    [Arguments(1501, false)]
    public async Task Acquire_NoOpOrInsufficientCapacityDoesNotChangeAnyStack(int amount, bool expected)
    {
        var bag = CreateFullBag();

        var acquired = bag.AcquireDefaultItemEx(ItemTaskType.Loot, _template.Id, amount, 0,
            out var created, out var updated, 0);

        await Assert.That(acquired).IsEqualTo(expected);
        await Assert.That(created).IsEmpty();
        await Assert.That(updated).IsEmpty();
        foreach (var item in bag.Items)
        {
            await Assert.That(item.Count).IsEqualTo(500);
            await Assert.That(item.IsDirty).IsFalse();
        }
    }

    private ItemContainer CreateFullBag()
    {
        var bag = new ItemContainer(0, SlotType.Inventory, false, null);
        // Reverse storage order to exercise the acquisition path's slot ordering.
        for (var slot = 2; slot >= 0; slot--)
        {
            var item = new Item((ulong)(slot + 1), _template, 500)
            {
                SlotType = SlotType.Inventory,
                Slot = slot,
                _holdingContainer = bag
            };
            item.IsDirty = false;
            bag.Items.Add(item);
        }
        bag.ContainerSize = 3;
        return bag;
    }
}
