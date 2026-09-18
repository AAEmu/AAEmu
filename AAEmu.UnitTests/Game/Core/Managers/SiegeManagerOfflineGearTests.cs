using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The gear score a siege raid team lists for a member who is not in the world. Their pieces come from the
/// equipment container the startup load already built; the lookup has to be a read, because the manager's
/// creating accessor registers an empty container for a character who has none, and the next save writes that
/// as an empty <c>item_containers</c> row.
/// </summary>
// The managers under test are process-wide singletons, and this class swaps three of them.
[NotInParallel]
public class SiegeManagerOfflineGearTests
{
    private const uint CharacterId = 4242;
    private const uint ArmorSlotTypeId = 5;

    /// <summary>The armor formula the scoring path evaluates for an armor piece.</summary>
    private const FormulaKind ArmorFormula = FormulaKind.GearScoreArmor;

    [Test]
    public async Task OfflineGear_ForACharacterWithNoContainer_IsZeroAndRegistersNone()
    {
        await MakeContainerIdsAvailable();
        var containers = new Dictionary<ulong, ItemContainer>();
        using var items = new SingletonScope<ItemManager>(CreateItemManager(containers));
        using var formulas = new SingletonScope<FormulaManager>(CreateFormulaManager());

        await Assert.That(OfflineGearScore(CharacterId)).IsEqualTo(0u);
        // Nothing was built for the character on the way: the save loop has no new container to write.
        await Assert.That(containers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OfflineGear_IsTheSumOfTheLoadedContainersPieces()
    {
        var containers = new Dictionary<ulong, ItemContainer>();
        var equipment = new ItemContainer(CharacterId, SlotType.Equipment, false, null) { ContainerId = 77 };
        equipment.Items.Add(ArmorPiece(1, 50));
        equipment.Items.Add(ArmorPiece(2, 30));
        containers.Add(equipment.ContainerId, equipment);

        using var items = new SingletonScope<ItemManager>(CreateItemManager(containers));
        using var formulas = new SingletonScope<FormulaManager>(CreateFormulaManager());

        // A weight of 1.00 (the stored 100 is hundredths) and no grade row, so the two pieces are worth their
        // levels - the same per-piece result a live character's total is built from.
        await Assert.That(OfflineGearScore(CharacterId)).IsEqualTo(80u);
        await Assert.That(containers.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OfflineGear_IgnoresAnotherCharactersEquipment()
    {
        await MakeContainerIdsAvailable();
        var containers = new Dictionary<ulong, ItemContainer>();
        var someoneElse = new ItemContainer(CharacterId + 1, SlotType.Equipment, false, null) { ContainerId = 78 };
        someoneElse.Items.Add(ArmorPiece(1, 50));
        containers.Add(someoneElse.ContainerId, someoneElse);

        using var items = new SingletonScope<ItemManager>(CreateItemManager(containers));
        using var formulas = new SingletonScope<FormulaManager>(CreateFormulaManager());

        await Assert.That(OfflineGearScore(CharacterId)).IsEqualTo(0u);
        await Assert.That(containers.Count).IsEqualTo(1);
    }

    private static EquipItem ArmorPiece(ulong id, byte level) => new(id, new ArmorTemplate
    {
        Id = 100 + (uint)id,
        Level = level,
        SlotTemplate = new WearableSlot { SlotTypeId = ArmorSlotTypeId, GearScoreMultiplier = 100 }
    }, 1);

    private static ItemManager CreateItemManager(Dictionary<ulong, ItemContainer> containers)
    {
        var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
        SetField(manager, "_grades", new Dictionary<int, GradeTemplate>());
        SetField(manager, "_holdables", new Dictionary<uint, Holdable>());
        SetField(manager, "_wearableSlots", new Dictionary<uint, WearableSlot>
        {
            [ArmorSlotTypeId] = new WearableSlot { SlotTypeId = ArmorSlotTypeId, GearScoreMultiplier = 100 }
        });
        SetField(manager, "_allPersistentContainers", containers);
        return manager;
    }

    private static FormulaManager CreateFormulaManager()
    {
        var manager = new FormulaManager();
        SetField(manager, "_formulas", new Dictionary<uint, Formula>
        {
            [(uint)ArmorFormula] = new("item_level * item_grade * gear_score_multiplier")
        });
        return manager;
    }

    /// <summary>
    /// Makes container ids available, so a lookup that builds a container can actually build one. Without it the
    /// creating accessor throws on its way to the id allocator, and a test could no longer tell a read from a
    /// build.
    /// </summary>
    private static async Task MakeContainerIdsAvailable()
    {
        var ids = ContainerIdManager.Instance;
        ids.SetUsedIdsLoaderForTest(() => []);
        await Assert.That(ids.Initialize()).IsTrue();
    }

    /// <summary>The manager's own scoring of a member who is not in the world.</summary>
    private static uint OfflineGearScore(uint characterId) =>
        (uint)typeof(SiegeManager).GetMethod("OfflineGearScore", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [characterId])!;

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"Missing field {name}");
    }
}
