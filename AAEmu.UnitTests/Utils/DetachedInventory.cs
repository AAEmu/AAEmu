using System.Runtime.CompilerServices;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.UnitTests.Utils;

/// <summary>Live inventory containers without the startup database/ID allocator.</summary>
internal static class DetachedInventory
{
    public static Inventory Create(Character character)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var containers = new Dictionary<SlotType, ItemContainer>();
        foreach (var (type, name) in new[]
                 {
                     (SlotType.Inventory, nameof(Inventory.Bag)),
                     (SlotType.Bank, nameof(Inventory.Warehouse)),
                     (SlotType.Equipment, nameof(Inventory.Equipment)),
                     (SlotType.System, nameof(Inventory.SystemContainer))
                 })
        {
            var container = new ItemContainer(character.Id, type, false, character)
            {
                ContainerId = (ulong)type + 100,
                ContainerSize = 20,
                Owner = character
            };
            containers.Add(type, container);
            typeof(Inventory).GetProperty(name)!.SetValue(inventory, container);
        }
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory, containers);
        character.Inventory = inventory;
        character.Equipment = inventory.Equipment;
        return inventory;
    }
}
