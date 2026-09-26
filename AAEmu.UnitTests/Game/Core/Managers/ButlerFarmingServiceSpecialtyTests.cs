using System.Runtime.CompilerServices;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class ButlerFarmingServiceSpecialtyTests
{
    private IServiceProvider _previousProvider;
    private ServiceProvider _questProvider;

    [Before(Test)]
    public void SetUpQuestProvider()
    {
        _previousProvider = SingletonContainer.ServiceProvider;
        _questProvider = new ServiceCollection()
            .AddSingleton(new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object))
            .BuildServiceProvider();
        SingletonContainer.ServiceProvider = _questProvider;
    }

    [After(Test)]
    public void RestoreQuestProvider()
    {
        SingletonContainer.ServiceProvider = _previousProvider;
        _questProvider.Dispose();
    }

    [Test]
    public async Task Start_CommitsTheTradeRowIdBeforeApplyingLiveState()
    {
        var harness = CreateHarness();
        var result = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(harness.Persistence.LastCandidate.SpecialtyType).IsEqualTo(TradeRowId);
        await Assert.That(harness.Butler.SpecialtyTradeJobs[1].SpecialtyType).IsEqualTo(TradeRowId);
        await Assert.That(harness.Butler.LaborPower).IsEqualTo(99u);
        await Assert.That(harness.Butler.RemainProductionCost).IsEqualTo((ushort)99);
        await Assert.That(harness.Material.Count).IsEqualTo(4);
        await Assert.That(harness.Persistence.RegisterCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Cancel_RemovesTheDurableRowBeforeRemovingTheLiveJob()
    {
        var harness = CreateHarness();
        harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);

        var result = harness.Service.CancelSpecialtyTrade(harness.Character, 1);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(harness.Persistence.CancelCalls).IsEqualTo(1);
        await Assert.That(harness.Persistence.Contains(1)).IsFalse();
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(1)).IsFalse();
    }

    [Test]
    public async Task Due_ReloadsAJobAfterRestartAndSettlesItExactlyOnce()
    {
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);

        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(harness.Persistence.SettleCalls).IsEqualTo(1);
        await Assert.That(harness.Settlement.CommitCalls).IsEqualTo(1);
        await Assert.That(harness.Persistence.Contains(job.JobId)).IsFalse();
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(job.JobId)).IsFalse();
    }

    [Test]
    public async Task Start_RejectsTheSameTradeRowAndZoneWithoutASecondWrite()
    {
        var harness = CreateHarness();
        var first = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);
        var duplicate = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);

        await Assert.That(first.Success).IsTrue();
        await Assert.That(duplicate.Success).IsFalse();
        await Assert.That(duplicate.Failure).IsEqualTo(ButlerFarmingOperationFailure.DuplicateSpecialtyTrade);
        await Assert.That(harness.Persistence.RegisterCalls).IsEqualTo(1);
        await Assert.That(harness.Material.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Start_ConcurrentRequestsCommitOnlyOneDurableRow()
    {
        var harness = CreateHarness();
        var first = Task.Run(() => harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId));
        var second = Task.Run(() => harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId));
        var results = await Task.WhenAll(first, second);

        await Assert.That(results.Count(result => result.Success)).IsEqualTo(1);
        await Assert.That(results.Count(result => !result.Success)).IsEqualTo(1);
        await Assert.That(harness.Persistence.RegisterCalls).IsEqualTo(1);
        await Assert.That(harness.Butler.SpecialtyTradeJobs.Count).IsEqualTo(1);
        await Assert.That(harness.Material.Count).IsEqualTo(4);
    }

    [Test]
    public async Task AmbiguousRegister_IsReconciledByReadingTheCommittedRow()
    {
        var harness = CreateHarness();
        harness.Persistence.NextRegister = new ButlerSpecialtyTradePersistResult(false, true, 1);

        var result = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(harness.Persistence.LoadCalls).IsEqualTo(1);
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(1)).IsTrue();
        await Assert.That(harness.Butler.LaborPower).IsEqualTo(99u);
    }

    [Test]
    public async Task AmbiguousCancelAndDueCommit_AreReconciledByDurableRead()
    {
        var harness = CreateHarness();
        harness.Persistence.NextRegister = new ButlerSpecialtyTradePersistResult(false, true, 1);
        var start = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);
        harness.Persistence.NextCancel = new ButlerSpecialtyTradePersistResult(false, true, 1);

        var cancel = harness.Service.CancelSpecialtyTrade(harness.Character, start.Job.JobId);

        await Assert.That(start.Success).IsTrue();
        await Assert.That(cancel.Success).IsTrue();
        await Assert.That(harness.Persistence.LoadCalls).IsEqualTo(2);
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(1)).IsFalse();

        var restartJob = new ButlerSpecialtyTradeJob(2, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(restartJob);
        harness.Persistence.AddDurable(restartJob);
        harness.Persistence.NextSettle = new ButlerSpecialtyTradePersistResult(false, true, 2);
        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(harness.Settlement.CommitCalls).IsEqualTo(1);
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(2)).IsFalse();
    }

    [Test]
    public async Task AmbiguousCancel_WithUnknownDurableStateLeavesTheJobForRetry()
    {
        var harness = CreateHarness();
        harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);
        harness.Persistence.NextCancel = new ButlerSpecialtyTradePersistResult(false, true, 1);
        harness.Persistence.NextLoadThrows = true;

        var result = harness.Service.CancelSpecialtyTrade(harness.Character, 1);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ButlerFarmingOperationFailure.ConcurrentChange);
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(1)).IsTrue();
    }

    [Test]
    public async Task LiveApplyFailure_ReconcilesThePersistedAggregate()
    {
        var harness = CreateHarness();
        harness.ItemManager.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Callback((ItemPersistenceSnapshot snapshot) =>
            {
                if (Interlocked.Exchange(ref harness.FailLiveApply, 1) == 0)
                    throw new InvalidOperationException("simulated live apply failure");
                snapshot.Item.Count = snapshot.Desired.Count;
            });

        var result = harness.Service.RegisterSpecialtyTrade(harness.Character, TradeRowId, ZoneId);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(1)).IsTrue();
        await Assert.That(harness.Butler.LaborPower).IsEqualTo(99u);
    }

    private const uint TradeRowId = 9;
    private const uint CraftId = 7001;
    private const short ZoneId = 5;
    private const uint ProductItemId = 7002;
    private const uint MaterialItemId = 7003;

    private static Harness CreateHarness()
    {
        EnsureQuestManager();
        var (inventory, material) = CreateInventory();
        var character = (Character)inventory.Owner;
        character.Connection = new GameConnection(Mock.Of<ISession>().Object);
        var butler = new CharacterButler(character.Id);
        butler.Apply(new CharacterButlerRecord(character.Id, 1, string.Empty, 100, 0, 100));

        var itemManager = Mock.Of<IItemManager>();
        itemManager.CapturePersistenceSnapshot(Any<Item>())
            .Returns((Item item) => ItemPersistenceSnapshot.Capture(item));
        itemManager.ApplyCommittedSnapshot(Any<ItemPersistenceSnapshot>())
            .Callback((ItemPersistenceSnapshot snapshot) => snapshot.Item.Count = snapshot.Desired.Count);
        var harness = new Harness
        {
            Character = character,
            Butler = butler,
            Material = material,
            ItemManager = itemManager,
            Persistence = new FakeSpecialtyPersistence(),
            Settlement = new FakeSettlement()
        };
        harness.Service = new ButlerFarmingService(
            new ButlerManagerStub(butler),
            new AdmissionStub(),
            Mock.Of<IButlerRepository>().Object,
            itemManager.Object,
            () => throw new InvalidOperationException("service attempted direct MySQL access"),
            () => DateTime.UnixEpoch.AddSeconds(200),
            harness.Settlement,
            _ => null,
            (minimum, _) => minimum,
            harness.Persistence);
        return harness;
    }

    private static void EnsureQuestManager()
    {
        if (SingletonContainer.ServiceProvider?.GetService<QuestManager>() is not null)
            return;
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object))
            .BuildServiceProvider();
    }

    private static (Inventory Inventory, Item Material) CreateInventory()
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 71 };
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var bag = new ItemContainer(character.Id, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = 501,
            ContainerSize = 50
        };
        var template = new ItemTemplate { Id = MaterialItemId, MaxCount = 1000 };
        var material = new Item(9001, template, 5)
        {
            OwnerId = character.Id,
            SlotType = SlotType.Inventory,
            Slot = 4,
            _holdingContainer = bag
        };
        bag.Items.Add(material);
        bag.UpdateFreeSlotCount();
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
            new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        character.Inventory = inventory;
        return (inventory, material);
    }

    private sealed class Harness
    {
        public required Character Character { get; init; }
        public required CharacterButler Butler { get; init; }
        public required Item Material { get; init; }
        public required Mock<IItemManager> ItemManager { get; init; }
        public required FakeSpecialtyPersistence Persistence { get; init; }
        public required FakeSettlement Settlement { get; init; }
        public ButlerFarmingService Service { get; set; } = null!;
        public int FailLiveApply;
    }

    private sealed class ButlerManagerStub(CharacterButler butler) : IButlerManager
    {
        public CharacterButler GetOrCreate(uint characterId) => butler.CharacterId == characterId ? butler : null;
        public IReadOnlyList<CharacterButler> SnapshotAll() => [butler];
        public ButlerOperationResult Bind(Character character, House house, Func<uint, House> resolver, bool notifyOwner = false) => default;
        public ButlerOperationResult Unbind(Character character, bool notifyOwner = false) => default;
        public bool UnbindHouse(uint houseId, bool notifyOwner = true) => false;
        public void RemoveCharacter(uint characterId) { }
        public ButlerPresentation GetPresentation(Character character) => default;
        public ButlerPresentation GetPresentation(Character character, Action<ButlerPresentation> publish) => default;
        public bool IsHouseBound(uint houseId) => houseId == butler.HouseId;
        public void Save(CharacterButler value, MySqlConnection connection, MySqlTransaction transaction) { }
    }

    private sealed class AdmissionStub : IButlerFarmingAdmissionResolver
    {
        public bool TryResolveHarvest(Character character, CharacterButler butler, uint staticHarvestId,
            out ButlerHarvestAdmissionContext context)
        {
            context = default;
            return false;
        }

        public bool TryResolveSpecialtyTrade(Character character, CharacterButler butler, uint tradeId,
            short zoneId, out ButlerSpecialtyTradeAdmissionContext context) =>
            TryResolveSpecialtyTrade(character, butler, tradeId, zoneId, out context, out _);

        public bool TryResolveSpecialtyTrade(Character character, CharacterButler butler, uint tradeId,
            short zoneId, out ButlerSpecialtyTradeAdmissionContext context,
            out ButlerSpecialtyTradeRules.AdmissionFailure failure)
        {
            context = default;
            failure = ButlerSpecialtyTradeRules.AdmissionFailure.InvalidContent;
            if (tradeId != TradeRowId || zoneId != ZoneId)
                return false;
            var template = new ButlerTemplate
            {
                Id = 1,
                TradeAvailableLevel = 1,
                DefaultSpecialtyTradeSlotCount = 2
            };
            var level = new ButlerLevel { ButlerId = 1, Level = 1 };
            var trade = new ButlerSpecialtyTradeDefinition(TradeRowId, 20, CraftId, 1, 1, 1,
                checked((uint)ZoneId));
            var craft = new Craft
            {
                Id = CraftId,
                SkillId = 8001,
                CraftProducts = [new CraftProduct { CraftId = CraftId, ItemId = ProductItemId, Amount = 1, Rate = 100 }],
                CraftMaterials = [new CraftMaterial { CraftId = CraftId, ItemId = MaterialItemId, Amount = 1 }]
            };
            // Delegate to the production rules with the butler's real active jobs, so the stub
            // enforces the same slot-exhaustion and duplicate rejection that the live resolver
            // does. Building the context by hand here would silently skip those checks.
            return ButlerSpecialtyTradeRules.TryCreateAdmissionContext(
                template,
                level,
                trade,
                craft,
                new SkillTemplate { Id = 8001, ConsumeLaborPower = 1 },
                butler.SpecialtyTradeJobs.Values.ToArray(),
                template.DefaultSpecialtyTradeSlotCount,
                out context,
                out failure);
        }

        public bool TryResolveNextGardenSlotExpansion(Character character, CharacterButler butler,
            out ButlerGardenSlotExpansionContext context)
        {
            context = default;
            return false;
        }

        public bool TryResolveNextSpecialtyTradeSlotExpansion(Character character, CharacterButler butler,
            out ButlerSpecialtyTradeSlotExpansionContext context)
        {
            context = default;
            return false;
        }
    }

    private sealed class FakeSpecialtyPersistence : IButlerSpecialtyTradePersistence
    {
        private readonly object gate = new();
        private readonly Dictionary<long, ButlerSpecialtyTradeJob> durable = [];
        private long nextId = 1;

        public ButlerSpecialtyTradeJobCandidate LastCandidate { get; private set; }
        public ButlerSpecialtyTradePersistResult? NextRegister { get; set; }
        public ButlerSpecialtyTradePersistResult? NextCancel { get; set; }
        public ButlerSpecialtyTradePersistResult? NextSettle { get; set; }
        public bool NextLoadThrows { get; set; }
        public int RegisterCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int SettleCalls { get; private set; }
        public int LoadCalls { get; private set; }

        public ButlerSpecialtyTradePersistResult Register(CharacterButlerRecord proposed,
            ButlerSpecialtyTradeJobCandidate candidate, IReadOnlyList<ItemPersistenceSnapshot> snapshots)
        {
            lock (gate)
            {
                RegisterCalls++;
                LastCandidate = candidate;
                if (NextRegister is { } configured)
                {
                    NextRegister = null;
                    if (configured.Ambiguous || configured.Success)
                        durable[configured.JobId] = Job(candidate, configured.JobId);
                    return configured;
                }
                var id = nextId++;
                durable[id] = Job(candidate, id);
                return new(true, false, id);
            }
        }

        public ButlerSpecialtyTradePersistResult Cancel(uint characterId, long jobId)
        {
            lock (gate)
            {
                CancelCalls++;
                if (NextCancel is { } configured)
                {
                    NextCancel = null;
                    if (configured.Ambiguous || configured.Success)
                        durable.Remove(jobId);
                    return configured;
                }
                durable.Remove(jobId);
                return new(true, false, jobId);
            }
        }

        public ButlerSpecialtyTradePersistResult Settle(uint characterId, long jobId, SpecialtyMarketWrite market)
        {
            lock (gate)
            {
                SettleCalls++;
                if (NextSettle is { } configured)
                {
                    NextSettle = null;
                    if (configured.Ambiguous || configured.Success)
                        durable.Remove(jobId);
                    return configured;
                }
                durable.Remove(jobId);
                return new(true, false, jobId);
            }
        }

        public bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job)
        {
            lock (gate)
            {
                LoadCalls++;
                if (NextLoadThrows)
                {
                    NextLoadThrows = false;
                    job = null;
                    throw new InvalidOperationException("simulated durable read failure");
                }
                return durable.TryGetValue(jobId, out job);
            }
        }

        public void AddDurable(ButlerSpecialtyTradeJob job)
        {
            lock (gate)
                durable[job.JobId] = job;
        }

        public bool Contains(long jobId)
        {
            lock (gate)
                return durable.ContainsKey(jobId);
        }

        private static ButlerSpecialtyTradeJob Job(ButlerSpecialtyTradeJobCandidate candidate, long id) =>
            new(id, candidate.NpcId, candidate.SpecialtyType, candidate.ToZoneGroupType,
                candidate.ProductItemId, candidate.CreatedTime, candidate.DeliveryTime);
    }

    private sealed class FakeSettlement : IButlerSpecialtyTradeSettlement
    {
        public int CommitCalls { get; private set; }

        public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, out SpecialtyMarketWrite market)
        {
            market = new SpecialtyMarketWrite(new SpecialtyMarketState { Revision = 1 },
                new SpecialtyMarketState { Revision = 2 });
            return true;
        }

        public void Apply(SpecialtyMarketWrite market, MySqlConnection connection, MySqlTransaction transaction) { }
        public void Commit(SpecialtyMarketWrite market) => CommitCalls++;
    }
}
