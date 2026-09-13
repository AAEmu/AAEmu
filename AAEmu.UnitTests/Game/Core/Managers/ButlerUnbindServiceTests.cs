using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ButlerUnbindServiceTests
{
    [Test]
    public async Task Unbind_RequiresPersistenceAndOperationGuards()
    {
        var service = CreateService(Mock.Of<IItemManager>().Object, out _);
        var butler = new CharacterButler(10) { HouseId = 20 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IButlerUnbindService)service).UnbindLocked(butler, 20, null));

        await Assert.That(exception.Message).Contains("persistence and operation guards");
        await Assert.That(butler.HouseId).IsEqualTo((uint)20);
    }

    [Test]
    public async Task Unbind_MissingStoredItemFailsBeforeDatabaseAndPreservesState()
    {
        const uint characterId = 10;
        var items = Mock.Of<IItemManager>();
        var system = new ItemContainer(characterId, SlotType.System, false, null);
        items.FindItemContainerFor(characterId, SlotType.System, 0).Returns(system);
        var fallbackGuardHeld = false;
        items.GetItemByItemId(500)
            .Callback((ulong _) => fallbackGuardHeld = Monitor.IsEntered(system))
            .Returns((Item)null);
        var service = CreateService(items.Object, out var openCount);
        var butler = new CharacterButler(characterId)
        {
            HouseId = 20,
            RemainProductionCost = 30
        };
        butler.ApplyStoredItem(new ButlerStoredItem(1, 500));

        ButlerUnbindServiceResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
                result = ((IButlerUnbindService)service).UnbindLocked(butler, 20, null);
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsEqualTo(ErrorMessageType.InternalError);
        await Assert.That(openCount()).IsEqualTo(0);
        await Assert.That(fallbackGuardHeld).IsTrue();
        await Assert.That(Monitor.IsEntered(system)).IsFalse();
        await Assert.That(butler.HouseId).IsEqualTo((uint)20);
        await Assert.That(butler.RemainProductionCost).IsEqualTo((ushort)30);
        await Assert.That(butler.StoredItems.ContainsKey(500)).IsTrue();
    }

    private static ButlerUnbindService CreateService(IItemManager itemManager, out Func<int> openCount)
    {
        var opens = 0;
        openCount = () => opens;
        return new ButlerUnbindService(
            Mock.Of<IButlerRepository>().Object,
            Mock.Of<IMailManager>().Object,
            itemManager,
            Mock.Of<INameManager>().Object)
        {
            OpenConnection = () =>
            {
                opens++;
                throw new InvalidOperationException("Database should not be opened by this test.");
            }
        };
    }
}
