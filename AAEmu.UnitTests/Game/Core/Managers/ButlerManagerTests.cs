using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ButlerManagerTests
{
    [Test]
    public async Task Bind_PersistsOwnedFinishedEligibleHouseBeforePublishingAssociation()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var house = CreateHouse(20, character.Id, true, 40);

        var result = manager.Bind(character, house, _ => house);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Error).IsEqualTo(ErrorMessageType.NoErrorMessage);
        await Assert.That(repository.Saved).HasCount().EqualTo(1);
        await Assert.That(repository.Saved[0].HouseId).IsEqualTo(house.Id);
        await Assert.That(manager.GetOrCreate(character.Id).HouseId).IsEqualTo(house.Id);
        await Assert.That(manager.IsHouseBound(house.Id)).IsTrue();

        var presentation = manager.GetPresentation(character, _ => house);
        await Assert.That(presentation.IsBound).IsTrue();
        await Assert.That(presentation.Info.OwnerId).IsEqualTo((ulong)character.Id);
        await Assert.That(presentation.Info.WorldId).IsEqualTo(CharacterBlocked.LocalWorldId);
        await Assert.That(presentation.Info.Name).IsEqualTo(string.Empty);
        await Assert.That(presentation.Info.HouseTlId).IsEqualTo(house.TlId);
        await Assert.That(presentation.HouseName).IsEqualTo(house.Name);
    }

    [Test]
    public async Task Bind_RejectsNonOwnerAndUnfinishedHouseWithoutWriting()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);

        var nonOwnerHouse = CreateHouse(20, 11, true, 40);
        var unfinishedHouse = CreateHouse(21, character.Id, false, 40);
        var nonOwner = manager.Bind(character, nonOwnerHouse, _ => nonOwnerHouse);
        var unfinished = manager.Bind(character, unfinishedHouse, _ => unfinishedHouse);

        await Assert.That(nonOwner.Error).IsEqualTo(ErrorMessageType.InteractionPermissionDeny);
        await Assert.That(unfinished.Error).IsEqualTo(ErrorMessageType.InteractionPermissionDeny);
        await Assert.That(repository.Saved).IsEmpty();
        await Assert.That(manager.GetOrCreate(character.Id).HouseId).IsEqualTo((uint)0);
    }

    [Test]
    public async Task Bind_DoesNotTreatGardenCapacityAsResidenceEligibility()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);

        var house = CreateHouse(20, character.Id, true, 0);
        var result = manager.Bind(character, house, _ => house);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(repository.Saved[^1].HouseId).IsEqualTo((uint)20);
    }

    [Test]
    public async Task Bind_DurableWriteFailureDoesNotPublishAssociation()
    {
        var repository = new RecordingRepository { ThrowOnSave = true };
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var house = CreateHouse(20, character.Id, true, 40);

        var result = manager.Bind(character, house, _ => house);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsEqualTo(ErrorMessageType.InternalError);
        await Assert.That(manager.GetOrCreate(character.Id).HouseId).IsEqualTo((uint)0);
        await Assert.That(manager.IsHouseBound(house.Id)).IsFalse();
    }

    [Test]
    public async Task Bind_RechecksOwnerAfterWaitingForHouseLifecycleTransition()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var house = CreateHouse(20, character.Id, true, 40);
        using var transitionEntered = new ManualResetEventSlim();
        using var finishTransition = new ManualResetEventSlim();
        var transition = Task.Run(() =>
        {
            lock (house.LifecycleSyncRoot)
            {
                transitionEntered.Set();
                finishTransition.Wait();
                house.OwnerId = 11;
            }
        });
        transitionEntered.Wait();

        var bind = Task.Run(() => manager.Bind(character, house, _ => house));
        finishTransition.Set();
        await transition;
        var result = await bind;

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsEqualTo(ErrorMessageType.InteractionPermissionDeny);
        await Assert.That(repository.Saved).IsEmpty();
    }

    [Test]
    public async Task Bind_RejectsHouseRemovedWhileRequestWaitedForLifecycleTransition()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var house = CreateHouse(20, character.Id, true, 40);
        using var transitionEntered = new ManualResetEventSlim();
        using var finishTransition = new ManualResetEventSlim();
        var removal = Task.Run(() =>
        {
            lock (house.LifecycleSyncRoot)
            {
                transitionEntered.Set();
                finishTransition.Wait();
                house.IsRemovedFromWorld = true;
            }
        });
        transitionEntered.Wait();

        var bind = Task.Run(() => manager.Bind(character, house, _ => null));
        finishTransition.Set();
        await removal;
        var result = await bind;

        await Assert.That(result.Success).IsFalse();
        await Assert.That(repository.Saved).IsEmpty();
    }

    [Test]
    public async Task Bind_RejectsSecondAssociationForTheSameCharacter()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);

        var firstHouse = CreateHouse(20, character.Id, true, 40);
        var secondHouse = CreateHouse(21, character.Id, true, 40);
        var first = manager.Bind(character, firstHouse, _ => firstHouse);
        var second = manager.Bind(character, secondHouse, _ => secondHouse);

        await Assert.That(first.Success).IsTrue();
        await Assert.That(second.Success).IsFalse();
        await Assert.That(second.Error).IsEqualTo(ErrorMessageType.AlreadyRequested);
        await Assert.That(repository.Saved).HasCount().EqualTo(1);
        await Assert.That(manager.GetOrCreate(character.Id).HouseId).IsEqualTo((uint)20);
    }

    [Test]
    public async Task Unbind_ClearsHouseAndRemainingProductionCostWhilePreservingOtherState()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var butler = manager.GetOrCreate(character.Id);
        butler.Name = "Mira";
        butler.LaborPower = 1234;
        butler.LpChargedAmount = 56;
        butler.RemainProductionCost = 78;
        var house = CreateHouse(20, character.Id, true, 40);
        manager.Bind(character, house, _ => house);

        var result = manager.Unbind(character);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(butler.HouseId).IsEqualTo((uint)0);
        await Assert.That(butler.Name).IsEqualTo("Mira");
        await Assert.That(butler.LaborPower).IsEqualTo((uint)1234);
        await Assert.That(butler.LpChargedAmount).IsEqualTo((ushort)56);
        await Assert.That(butler.RemainProductionCost).IsEqualTo((ushort)0);
        await Assert.That(repository.Saved[^1]).IsEqualTo(
            new CharacterButlerRecord(character.Id, 0, "Mira", 1234, 56, 0));

        var freePresentation = manager.GetPresentation(character, _ => null);
        await Assert.That(freePresentation.IsBound).IsFalse();
        await Assert.That(freePresentation.Info.OwnerId).IsEqualTo((ulong)0);
        await Assert.That(freePresentation.Info.WorldId).IsEqualTo(CharacterBlocked.LocalWorldId);
        await Assert.That(freePresentation.Info.Name).IsEqualTo("Mira");
        await Assert.That(freePresentation.Info.HouseTlId).IsEqualTo((ushort)0);
        await Assert.That(freePresentation.Info.LaborPower).IsEqualTo((uint)1234);
        await Assert.That(freePresentation.Info.LpChargedAmount).IsEqualTo((ushort)56);
        await Assert.That(freePresentation.Info.RemainProductionCost).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task Presentation_NewUnboundFarmhandProducesInitializedFreeState()
    {
        var manager = new ButlerManager(new RecordingRepository());

        var presentation = manager.GetPresentation(CreateCharacter(10), _ => null);

        await Assert.That(presentation.IsBound).IsFalse();
        await Assert.That(presentation.HouseName).IsEqualTo(string.Empty);
        await Assert.That(presentation.Info.OwnerId).IsEqualTo((ulong)0);
        await Assert.That(presentation.Info.WorldId).IsEqualTo(CharacterBlocked.LocalWorldId);
        await Assert.That(presentation.Info.HouseTlId).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task UnbindHouse_DurablyClearsAssociationUsedBySaleAndDemolition()
    {
        var repository = new RecordingRepository();
        var manager = new ButlerManager(repository);
        var character = CreateCharacter(10);
        var house = CreateHouse(20, character.Id, true, 40);
        manager.Bind(character, house, _ => house);

        var result = manager.UnbindHouse(house.Id, false);

        await Assert.That(result).IsTrue();
        await Assert.That(manager.IsHouseBound(house.Id)).IsFalse();
        await Assert.That(manager.GetOrCreate(character.Id).HouseId).IsEqualTo((uint)0);
        await Assert.That(repository.Saved[^1].HouseId).IsEqualTo((uint)0);
    }

    [Test]
    public async Task Presentation_ClearsPersistedAssociationWhenHouseIsMissing()
    {
        var initial = new CharacterButlerRecord(10, 20, "Mira", 1234, 56, 78);
        var repository = new RecordingRepository([initial]);
        var manager = new ButlerManager(repository);
        manager.Load();

        var presentation = manager.GetPresentation(CreateCharacter(10), _ => null);

        await Assert.That(presentation.IsBound).IsFalse();
        await Assert.That(presentation.Info.OwnerId).IsEqualTo((ulong)0);
        await Assert.That(presentation.Info.HouseTlId).IsEqualTo((ushort)0);
        await Assert.That(presentation.HouseName).IsEqualTo(string.Empty);
        await Assert.That(manager.GetOrCreate(10).HouseId).IsEqualTo((uint)0);
        await Assert.That(repository.Saved[^1]).IsEqualTo(initial with { HouseId = 0, RemainProductionCost = 0 });
    }

    private static Character CreateCharacter(uint id) => new(new UnitCustomModelParams()) { Id = id };

    private static House CreateHouse(uint id, uint ownerId, bool finished, ushort butlerGardenSize)
    {
        var house = new House
        {
            Id = id,
            OwnerId = ownerId,
            TlId = (ushort)(100 + id),
            Name = $"House {id}",
            Template = new HousingTemplate
            {
                HousingSize = new HousingSize { ButlerGardenSize = butlerGardenSize }
            }
        };
        typeof(House).GetField("_currentStep",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(house, finished ? -1 : 0);
        return house;
    }

    private sealed class RecordingRepository(IReadOnlyList<CharacterButlerRecord> initial = null)
        : IButlerRepository
    {
        public List<CharacterButlerRecord> Saved { get; } = [];
        public bool ThrowOnSave { get; init; }

        public IReadOnlyList<CharacterButlerRecord> LoadAll() => initial ?? [];

        public bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("Simulated durable write failure");
            Saved.Add(record);
            return true;
        }

        public void Save(CharacterButlerRecord record, MySqlConnection connection, MySqlTransaction transaction) =>
            Saved.Add(record);

        public void Delete(uint characterId) { }
    }
}
