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
using AAEmu.Game.Models.Game.Mails;
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

    [Test]
    public async Task Due_SettlesTheOwnerPayoutInTheSameCallAsTheJobDelete()
    {
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);

        harness.Service.ProcessDueSpecialtyTradeJobs();

        // The letter, not a bare delete: the settlement persists the money the delivery earned.
        await Assert.That(harness.Persistence.SettleCalls).IsEqualTo(1);
        await Assert.That(harness.Persistence.LastSettledMail).IsNotNull();
        await Assert.That(harness.Persistence.LastSettledMail!.Body.CopperCoins).IsEqualTo(FakePayoutAmount);
        await Assert.That(harness.Persistence.LastSettledMail.Header.ReceiverId).IsEqualTo(harness.Character.Id);
        await Assert.That(harness.Persistence.LastSettledQuote).IsNotNull();
        await Assert.That(harness.Persistence.LastSettledQuote!.Market).IsNotNull();
        await Assert.That(harness.Persistence.Contains(job.JobId)).IsFalse();
    }

    [Test]
    public async Task Due_CarriesThePreparedPreDeliveryQuoteThroughWithoutRereadingIt()
    {
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);

        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(harness.Settlement.PrepareCalls).IsEqualTo(1);
        // The goods are as old as the job's own 50s delivery. The settle instant is unix 200 while
        // the job was created at unix 100, and the 100s between them is scan lag the owner must not
        // pay a freshness bucket for, so the delivery duration is what the settlement is given.
        await Assert.That(harness.Settlement.LastFreshnessElapsedSeconds).IsEqualTo(50);
        await Assert.That(harness.Settlement.LastOwnerId).IsEqualTo(harness.Character.Id);
        await Assert.That(harness.Settlement.LastSettledAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(harness.Settlement.LastSettledAtUtc)
            .IsEqualTo(new DateTime(1970, 1, 1, 0, 3, 20, DateTimeKind.Utc));
        // The quote the settlement persisted is the very quote that was prepared, so the payout
        // keeps the ratio that was read before this delivery touched the market.
        await Assert.That(harness.Persistence.LastSettledQuote).IsSameReferenceAs(harness.Settlement.LastQuote);
        await Assert.That(harness.Persistence.LastSettledQuote!.DisplayedRatioPercent)
            .IsEqualTo(FakePreRatioPercent);
        await Assert.That(harness.Settlement.CommitCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Due_PublishesPersistedItemsBeforeThePreparedLetterBatch()
    {
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);
        var order = new List<string>();
        harness.ItemManager.PublishPersistedItems(Any<IEnumerable<Item>>())
            .Callback((IEnumerable<Item> _) => order.Add("items"));

        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(order).IsEquivalentTo(new[] { "items" });
        // Prepare, then publish: the letter is staged before the transaction and only made
        // visible after it committed.
        await Assert.That(harness.Payout.Calls).IsEquivalentTo(new[] { "prepare", "publish" });
    }

    [Test]
    public async Task Due_AnAmbiguousCommitThatLandedNeverReleasesTheCommittedMailId()
    {
        // The transaction committed and only its acknowledgement was lost: the job row is gone and
        // the payout letter is durable. Cancelling the batch here would release that letter's mail
        // id, and the next mail would be free to take the same id and overwrite a paid-out owner.
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);
        harness.Persistence.NextSettle = new ButlerSpecialtyTradePersistResult(false, true, job.JobId);

        harness.Service.ProcessDueSpecialtyTradeJobs();

        // A committed payout is published, never cancelled: the id has to stay reserved to it.
        await Assert.That(harness.Payout.Calls).IsEquivalentTo(new[] { "prepare", "publish" });
        await Assert.That(harness.Payout.CancelledBatches).IsEqualTo(0);
        // The retry read is what decides this, so it has to happen before anything is released.
        await Assert.That(harness.Persistence.LoadCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Due_AFailedSettlementReleasesTheStagedLetterForTheRetry()
    {
        // The counterpart: a settlement that really did not commit gives the job up, and the staged
        // letter is released with it so the retry does not leak a reserved id.
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);
        harness.Persistence.NextSettle = new ButlerSpecialtyTradePersistResult(false, false, job.JobId);

        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(harness.Payout.Calls).IsEquivalentTo(new[] { "prepare", "cancel" });
        await Assert.That(harness.Payout.CancelledBatches).IsEqualTo(1);
        await Assert.That(harness.Persistence.Contains(job.JobId)).IsTrue();
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(job.JobId)).IsTrue();
    }

    [Test]
    public async Task Due_PricesFreshnessFromTheJobDeliveryNotTheInstantTheScanReachedIt()
    {
        // Two scans of the same job: one that runs the moment the job is due and one that runs
        // long afterwards. The goods are the same age in both, so the payout must not change --
        // otherwise scan lag and a restart cost the owner a freshness bucket.
        var dueNow = CreateHarness();
        var dueNowJob = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        dueNow.Butler.ApplySpecialtyTradeJob(dueNowJob);
        dueNow.Persistence.AddDurable(dueNowJob);
        dueNow.Service.ProcessDueSpecialtyTradeJobs();

        var late = CreateHarness();
        var lateJob = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        late.Butler.ApplySpecialtyTradeJob(lateJob);
        late.Persistence.AddDurable(lateJob);
        late.UtcNow = DateTime.UnixEpoch.AddSeconds(100 + 50 + 3600);
        late.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(dueNow.Settlement.LastFreshnessElapsedSeconds).IsEqualTo(50);
        await Assert.That(late.Settlement.LastFreshnessElapsedSeconds).IsEqualTo(50);
    }

    [Test]
    public async Task Due_WithoutAPreparedQuoteLeavesTheJobForRetry()
    {
        var harness = CreateHarness();
        var job = new ButlerSpecialtyTradeJob(1, 20, TradeRowId, (ushort)ZoneId, ProductItemId, 100, 50);
        harness.Butler.ApplySpecialtyTradeJob(job);
        harness.Persistence.AddDurable(job);
        harness.Settlement.PrepareFails = true;

        harness.Service.ProcessDueSpecialtyTradeJobs();

        await Assert.That(harness.Persistence.SettleCalls).IsEqualTo(0);
        await Assert.That(harness.Settlement.CommitCalls).IsEqualTo(0);
        await Assert.That(harness.Persistence.Contains(job.JobId)).IsTrue();
        await Assert.That(harness.Butler.SpecialtyTradeJobs.ContainsKey(job.JobId)).IsTrue();
    }

    private const uint TradeRowId = 9;
    private const uint CraftId = 7001;
    private const short ZoneId = 5;
    private const uint ProductItemId = 7002;
    private const uint MaterialItemId = 7003;
    private const string OwnerName = "Owner";
    private const int FakeBasePrice = 1000;
    private const int FakePreRatioPercent = 100;
    private const uint FreshnessRewardRate = 1000;
    private const int FakePayoutAmount = 1020;

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
        var mailManager = Mock.Of<IMailManager>();
        var harness = new Harness
        {
            Character = character,
            Butler = butler,
            Material = material,
            ItemManager = itemManager,
            Payout = new RecordingPayoutPublisher(),
            Persistence = new FakeSpecialtyPersistence(),
            Settlement = new FakeSettlement()
        };
        harness.Service = new ButlerFarmingService(
            new ButlerManagerStub(butler),
            new AdmissionStub(),
            Mock.Of<IButlerRepository>().Object,
            itemManager.Object,
            () => throw new InvalidOperationException("service attempted direct MySQL access"),
            () => harness.UtcNow,
            harness.Settlement,
            _ => null,
            (minimum, _) => minimum,
            harness.Persistence,
            harness.Payout,
            _ => OwnerName);
        return harness;
    }

    /// <summary>
    /// Records what the settlement asked of the mail layer, in order, and stages a real
    /// <see cref="PreparedMailBatch"/> the way the live mail manager does: the batch reserves ids
    /// and only a publish makes it visible.
    /// </summary>
    private sealed class RecordingPayoutPublisher : IButlerSpecialtyTradePayoutPublisher
    {
        public List<string> Calls { get; } = [];
        public PreparedMailBatch LastBatch { get; private set; }
        public IReadOnlyList<BaseMail> LastPersisted { get; private set; }
        public int NextMailId = 5000;
        public bool PrepareResult { get; set; } = true;
        public bool PublishResult { get; set; } = true;

        /// <summary>How often a staged letter gave its reserved ids back.</summary>
        public int CancelledBatches { get; private set; }

        public bool TryPrepareBatch(IReadOnlyList<BaseMail> mails, out PreparedMailBatch batch)
        {
            Calls.Add("prepare");
            batch = null;
            if (!PrepareResult || mails == null || mails.Count == 0)
                return false;
            foreach (var mail in mails)
            {
                if (mail.Id <= 0)
                    mail.Id = NextMailId++;
            }
            LastBatch = batch = new PreparedMailBatch(
                [.. mails],
                mails.Select(mail => mail.ReceiverName).ToList(),
                []);
            return true;
        }

        public void PersistPreparedBatch(IReadOnlyList<BaseMail> mails, MySqlConnection connection,
            MySqlTransaction transaction) => LastPersisted = mails;

        public bool PublishPreparedBatch(PreparedMailBatch batch, bool alreadyPersisted = false)
        {
            Calls.Add("publish");
            LastBatch = batch;
            return PublishResult;
        }

        public void CancelPreparedBatch(PreparedMailBatch batch)
        {
            Calls.Add("cancel");
            CancelledBatches++;
            LastBatch = batch;
        }
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
        public required RecordingPayoutPublisher Payout { get; init; }
        public required FakeSpecialtyPersistence Persistence { get; init; }
        public required FakeSettlement Settlement { get; init; }
        public ButlerFarmingService Service { get; set; } = null!;
        public int FailLiveApply;

        /// <summary>The scan clock. Settling the same job at a later instant must not reprice it.</summary>
        public DateTime UtcNow { get; set; } = DateTime.UnixEpoch.AddSeconds(200);
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
        public ButlerSpecialtyTradeDeliveryQuote LastSettledQuote { get; private set; }
        public BaseMail LastSettledMail { get; private set; }

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

        public ButlerSpecialtyTradePersistResult Settle(uint characterId, long jobId,
            ButlerSpecialtyTradeDeliveryQuote quote, BaseMail ownerPayoutMail)
        {
            lock (gate)
            {
                SettleCalls++;
                LastSettledQuote = quote;
                LastSettledMail = ownerPayoutMail;
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
        public int PrepareCalls { get; private set; }
        public bool PrepareFails { get; set; }
        public long LastFreshnessElapsedSeconds { get; private set; }
        public uint LastOwnerId { get; private set; }
        public string LastOwnerName { get; private set; }
        public DateTime LastSettledAtUtc { get; private set; }
        public ButlerSpecialtyTradeDeliveryQuote LastQuote { get; private set; }
        public BaseMail LastPayoutMail { get; private set; }

        public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId,
            out SpecialtyMarketWrite market)
        {
            market = new SpecialtyMarketWrite(new SpecialtyMarketState { Revision = 1 },
                new SpecialtyMarketState { Revision = 2 });
            return true;
        }

        /// <summary>
        /// Stands in for the content-backed quote. It does not recompute money — the payout figures
        /// are pinned by the specialty market tests against real content — it only proves the
        /// service carries the prepared quote and letter through the settlement unchanged.
        /// </summary>
        public bool TryPrepare(uint npcId, uint productItemId, uint zoneGroupId, long freshnessElapsedSeconds,
            uint ownerId, string ownerName, DateTime settledAtUtc,
            out ButlerSpecialtyTradeDeliveryQuote quote, out BaseMail ownerPayoutMail)
        {
            PrepareCalls++;
            LastFreshnessElapsedSeconds = freshnessElapsedSeconds;
            LastOwnerId = ownerId;
            LastOwnerName = ownerName;
            LastSettledAtUtc = settledAtUtc;
            if (PrepareFails)
            {
                quote = null;
                ownerPayoutMail = null;
                return false;
            }

            var market = new SpecialtyMarketWrite(new SpecialtyMarketState { Revision = 1 },
                new SpecialtyMarketState { Revision = 2 });
            var mail = new BaseMail { MailType = MailType.SysSellBackpack, ReceiverName = ownerName };
            mail.Header.ReceiverId = ownerId;
            mail.AttachMoney(FakePayoutAmount);
            LastPayoutMail = mail;
            ownerPayoutMail = mail;
            LastQuote = quote = new ButlerSpecialtyTradeDeliveryQuote(
                market, productItemId, npcId, zoneGroupId, FakeBasePrice, FakePreRatioPercent,
                FreshnessRewardRate, 1f, 0d, 0d, 0, 0, Item.Coins, FakePayoutAmount, FakePayoutAmount,
                FakeBasePrice);
            return true;
        }

        public void Apply(ButlerSpecialtyTradeDeliveryQuote quote, MySqlConnection connection,
            MySqlTransaction transaction) => AppliedQuotes.Add(quote);

        public List<ButlerSpecialtyTradeDeliveryQuote> AppliedQuotes { get; } = [];

        public void Commit(ButlerSpecialtyTradeDeliveryQuote quote) => CommitCalls++;
    }
}
