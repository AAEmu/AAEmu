using System.Runtime.CompilerServices;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class InventoryMutationTests
{
    [Test]
    public async Task BagConsumptionPlan_DoesNotMutateUntilCommittedApply()
    {
        var (inventory, _, item) = CreateInventory(itemCount: 5);
        var itemManager = CreateItemManager();
        bool planned;
        ItemConsumptionPlan plan;
        int countBeforeApply;
        int countAfterApply;
        using (inventory.AcquireMutation())
        {
            planned = inventory.TryPlanBagConsumption(item.TemplateId, 2, out plan);
            countBeforeApply = item.Count;
            _ = plan.CapturePersistenceSnapshots(itemManager.Object);
            PersistenceGate.EnterOperation();
            try
            {
                _ = plan.ApplyCommitted(ItemTaskType.Invalid);
            }
            finally
            {
                PersistenceGate.ExitOperation();
            }
            countAfterApply = item.Count;
        }

        await Assert.That(planned).IsTrue();
        await Assert.That(countBeforeApply).IsEqualTo(5);
        await Assert.That(plan.Entries).HasCount().EqualTo(1);
        await Assert.That(plan.Entries[0].ExpectedCount).IsEqualTo(5);
        await Assert.That(plan.Entries[0].RemainingCount).IsEqualTo(3);
        await Assert.That(countAfterApply).IsEqualTo(3);
    }

    [Test]
    public async Task BagConsumptionPlan_RevalidatesEveryStackBeforeMutatingAny()
    {
        var (inventory, _, item) = CreateInventory(itemCount: 5);
        var itemManager = CreateItemManager();
        var planned = false;
        var rejected = false;
        var countAfterRejection = 0;
        using (inventory.AcquireMutation())
        {
            planned = inventory.TryPlanBagConsumption(item.TemplateId, 2, out var plan);
            _ = plan.CapturePersistenceSnapshots(itemManager.Object);
            item.Count = 4;
            try
            {
                _ = plan.ApplyCommitted(ItemTaskType.Invalid);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            countAfterRejection = item.Count;
        }

        await Assert.That(planned).IsTrue();
        await Assert.That(rejected).IsTrue();
        await Assert.That(countAfterRejection).IsEqualTo(4);
    }

    [Test]
    public async Task ExactBagConsumptionPlan_DoesNotSubstituteAnotherStackOfTheSameTemplate()
    {
        var (inventory, bag, selected) = CreateInventory(itemCount: 1);
        var alternate = new Item(9002, selected.Template, 10)
        {
            OwnerId = selected.OwnerId,
            SlotType = SlotType.Inventory,
            Slot = 5,
            _holdingContainer = bag
        };
        bag.Items.Add(alternate);
        bool selectedPlanned;
        bool alternatePlanned;
        ItemConsumptionPlan alternatePlan;
        using (inventory.AcquireMutation())
        {
            selectedPlanned = inventory.TryPlanExactBagConsumption(selected.Id, 2, out _);
            alternatePlanned = inventory.TryPlanExactBagConsumption(alternate.Id, 2, out alternatePlan);
        }

        await Assert.That(selectedPlanned).IsFalse();
        await Assert.That(alternatePlanned).IsTrue();
        await Assert.That(alternatePlan.Entries).HasCount().EqualTo(1);
        await Assert.That(alternatePlan.Entries[0].Item).IsSameReferenceAs(alternate);
        await Assert.That(alternatePlan.Entries[0].RemainingCount).IsEqualTo(8);
    }

    [Test]
    public async Task FarmhandMutation_RejectsForeignActiveSkillEffect()
    {
        var inventory = CreateBareInventory();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var effect = Task.Run(() =>
        {
            using var activity = inventory.EnterSkillEffect();
            entered.Set();
            release.Wait();
        });
        entered.Wait();

        var acquired = inventory.TryAcquireFarmhandMutation(out var mutation);
        try
        {
            await Assert.That(acquired).IsFalse();
            await Assert.That(mutation).IsNull();
        }
        finally
        {
            mutation?.Dispose();
            release.Set();
            await effect;
        }
    }

    [Test]
    public async Task FarmhandMutation_AllowsCurrentSkillEffectToPayItsOwnCost()
    {
        var inventory = CreateBareInventory();
        bool acquired;
        InventoryMutationLease mutation;
        using (inventory.EnterSkillEffect())
        {
            acquired = inventory.TryAcquireFarmhandMutation(out mutation);
            mutation?.Dispose();
        }

        await Assert.That(acquired).IsTrue();
        await Assert.That(mutation).IsNotNull();
    }

    [Test]
    public async Task FarmhandMutation_HoldsNewSkillEffectsUntilCommittedWindowCloses()
    {
        var inventory = CreateBareInventory();
        using var mutationEntered = new ManualResetEventSlim();
        using var releaseMutation = new ManualResetEventSlim();
        var mutationAcquired = false;
        var mutation = Task.Run(() =>
        {
            mutationAcquired = inventory.TryAcquireFarmhandMutation(out var lease);
            mutationEntered.Set();
            if (!mutationAcquired)
                return;
            releaseMutation.Wait();
            lease.Dispose();
        });
        mutationEntered.Wait();

        var attempting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effect = Task.Run(() =>
        {
            attempting.SetResult();
            using var activity = inventory.EnterSkillEffect();
            entered.SetResult();
        });

        await attempting.Task;
        await Task.Delay(25);
        try
        {
            await Assert.That(mutationAcquired).IsTrue();
            await Assert.That(entered.Task.IsCompleted).IsFalse();
        }
        finally
        {
            releaseMutation.Set();
        }

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.WhenAll(mutation, effect);
    }

    [Test]
    public async Task GenericSwap_RejectsDeclaredSystemLocationBeforeItemLookup()
    {
        var inventory = CreateBareInventory();

        var moved = inventory.SplitOrMoveItemEx(
            ItemTaskType.SwapItems,
            null,
            null,
            0,
            SlotType.System,
            0,
            0,
            SlotType.Inventory,
            1);

        await Assert.That(moved).IsFalse();

        var reversed = inventory.SplitOrMoveItemEx(
            ItemTaskType.SwapItems,
            null,
            null,
            0,
            SlotType.Inventory,
            0,
            0,
            SlotType.System,
            1);
        await Assert.That(reversed).IsFalse();
    }

    [Test]
    public async Task GenericSwap_RejectsActualSystemContainerHiddenBehindBagLocation()
    {
        var inventory = CreateBareInventory();
        var system = new ItemContainer(71, SlotType.System, false, null);
        var bag = new ItemContainer(71, SlotType.Inventory, false, null);

        var moved = inventory.SplitOrMoveItemEx(
            ItemTaskType.SwapItems,
            system,
            bag,
            0,
            SlotType.Inventory,
            0,
            0,
            SlotType.Inventory,
            1);

        await Assert.That(moved).IsFalse();

        var reversed = inventory.SplitOrMoveItemEx(
            ItemTaskType.SwapItems,
            bag,
            system,
            0,
            SlotType.Inventory,
            0,
            0,
            SlotType.Inventory,
            1);
        await Assert.That(reversed).IsFalse();
    }

    [Test]
    public async Task ConsumptionPublication_QueuesPacketsBeforeWaitingMoveAndCallbacksRequireRelease()
    {
        var (inventory, _, _) = CreateInventory(itemCount: 5);
        var character = (Character)inventory.Owner;
        var sentByThreads = new List<int>();
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] _) =>
        {
            lock (sentByThreads)
                sentByThreads.Add(Environment.CurrentManagedThreadId);
        });
        character.Connection = new GameConnection(session.Object);

        var publication = new ItemConsumptionPublication(
            inventory,
            inventory.Bag,
            ItemTaskType.Invalid,
            [],
            [],
            [new PublicationOrderPacket(1)],
            []);
        using var mutationEntered = new ManualResetEventSlim();
        using var moveAttempted = new ManualResetEventSlim();
        var publisherThreadId = 0;
        var moverThreadId = 0;
        var callbacksRejectedUnderLease = false;

        var publisher = Task.Run(() =>
        {
            lock (inventory.MutationSyncRoot)
            {
                publisherThreadId = Environment.CurrentManagedThreadId;
                mutationEntered.Set();
                moveAttempted.Wait();
                publication.PublishPackets();
                try
                {
                    publication.PublishCallbacks();
                }
                catch (InvalidOperationException)
                {
                    callbacksRejectedUnderLease = true;
                }
            }
        });
        var mover = Task.Run(() =>
        {
            mutationEntered.Wait();
            moveAttempted.Set();
            lock (inventory.MutationSyncRoot)
            {
                moverThreadId = Environment.CurrentManagedThreadId;
                character.SendPacket(new PublicationOrderPacket(2));
            }
        });

        await Task.WhenAll(publisher, mover).WaitAsync(TimeSpan.FromSeconds(1));
        publication.PublishCallbacks();

        await Assert.That(callbacksRejectedUnderLease).IsTrue();
        await Assert.That(sentByThreads).HasCount().EqualTo(2);
        await Assert.That(sentByThreads[0]).IsEqualTo(publisherThreadId);
        await Assert.That(sentByThreads[1]).IsEqualTo(moverThreadId);
    }

    private static Inventory CreateBareInventory()
        => (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));

    private static Mock<IItemManager> CreateItemManager()
    {
        var itemManager = Mock.Of<IItemManager>();
        itemManager.CapturePersistenceSnapshot(Any<Item>())
            .Returns((Item item) => ItemPersistenceSnapshot.Capture(item));
        itemManager.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Callback((ItemPersistenceSnapshot snapshot) => snapshot.Item.Count = snapshot.Desired.Count);
        return itemManager;
    }

    private sealed class PublicationOrderPacket(byte marker) : GamePacket(1, 1)
    {
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(marker);
            return stream;
        }
    }

    private static (Inventory Inventory, ItemContainer Bag, Item Item) CreateInventory(int itemCount)
    {
        const uint characterId = 71;
        const uint templateId = 26744;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var inventory = CreateBareInventory();
        typeof(Inventory).GetField(nameof(Inventory.Owner))!
            .SetValue(inventory, character);
        var bag = new ItemContainer(characterId, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = 501,
            ContainerSize = 50
        };
        var template = new ItemTemplate { Id = templateId, MaxCount = 1000 };
        var item = new Item(9001, template, itemCount)
        {
            OwnerId = characterId,
            SlotType = SlotType.Inventory,
            Slot = 4,
            _holdingContainer = bag
        };
        bag.Items.Add(item);
        bag.UpdateFreeSlotCount();

        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!
            .SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!
            .SetValue(inventory, new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        character.Inventory = inventory;
        return (inventory, bag, item);
    }
}
