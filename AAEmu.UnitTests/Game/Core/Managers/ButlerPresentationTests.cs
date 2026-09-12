using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ButlerPresentationTests
{
    [Test]
    public async Task BoundPresentation_ContainsDurableGardenPermanentAndHarvestState()
    {
        const uint characterId = 10;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var system = new ItemContainer(characterId, SlotType.System, false, character)
        {
            Owner = character,
            ContainerId = 502,
            ContainerSize = -1
        };
        typeof(Inventory).GetProperty(nameof(Inventory.SystemContainer))!.SetValue(inventory, system);
        character.Inventory = inventory;
        var item = new Item(9001, new ItemTemplate { Id = 15566, MaxCount = 1 }, 1)
        {
            OwnerId = characterId,
            SlotType = SlotType.System,
            Slot = 19,
            _holdingContainer = system
        };
        system.Items.Add(item);
        var items = Mock.Of<IItemManager>();
        items.GetItemByItemId(item.Id).Returns(item);
        var manager = new ButlerManager(
            Mock.Of<IButlerRepository>().Object,
            Mock.Of<IButlerUnbindService>().Object,
            items.Object);
        var butler = manager.GetOrCreate(characterId);
        butler.HouseId = 20;
        butler.ApplyPermanentData(1, 1234);
        butler.ApplyStoredItem(new ButlerStoredItem(2, item.Id));
        butler.ApplyHarvestJob(new ButlerHarvestJob(71, 5, 3, 2, 14, 99));
        var house = FinishedHouse(20, characterId);

        var presentation = manager.GetPresentation(character, _ => house);

        await Assert.That(presentation.IsBound).IsTrue();
        await Assert.That(presentation.Info.BagItems).HasCount().EqualTo(1);
        await Assert.That(presentation.Info.BagItems[0]).IsSameReferenceAs(item);
        await Assert.That(presentation.Info.PermanentDatas[1]).IsEqualTo(1234UL);
        await Assert.That(presentation.Info.HarvestDatas[71].HarvestId).IsEqualTo(5u);
        await Assert.That(presentation.Info.HarvestDatas[71].RequestedAmount).IsEqualTo((short)3);
    }

    [Test]
    public async Task FreePresentation_RetainsPermanentProgressAndOmitsResidenceCollections()
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 10 };
        var manager = new ButlerManager(
            Mock.Of<IButlerRepository>().Object,
            Mock.Of<IButlerUnbindService>().Object,
            Mock.Of<IItemManager>().Object);
        var butler = manager.GetOrCreate(character.Id);
        butler.ApplyPermanentData(1, 1234);
        butler.ApplyStoredItem(new ButlerStoredItem(2, 9001));
        butler.ApplyHarvestJob(new ButlerHarvestJob(71, 5, 3, 2, 14, 99));

        var presentation = manager.GetPresentation(character, _ => null);

        await Assert.That(presentation.IsBound).IsFalse();
        await Assert.That(presentation.Info.OwnerId).IsEqualTo(0UL);
        await Assert.That(presentation.Info.PermanentDatas[1]).IsEqualTo(1234UL);
        await Assert.That(presentation.Info.BagItems).IsEmpty();
        await Assert.That(presentation.Info.HarvestDatas).IsEmpty();
    }

    private static House FinishedHouse(uint id, uint ownerId)
    {
        var house = new House { Id = id, OwnerId = ownerId, TlId = 120 };
        typeof(House).GetField("_currentStep",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(house, -1);
        return house;
    }
}
