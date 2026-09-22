using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.PlotAuctions;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils.Mocks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Escrow, refunds and settlement through the real <see cref="PlotAuctionManager"/> against the
/// store interface, mirroring the craft-order escrow tests: a bid takes money and writes its
/// row, an outbid/exit/settlement hands the row's money over exactly once (wallet while the
/// character is online, letter when they are not), settlement transfers the configured prize
/// and refuses to run twice, and the whole state machine round-trips through the store the way
/// a restart reloads it. Content (price, increment, winner count, rewards, windows) comes from
/// the shipped column layout through the real loader, row values are this fixture's own.
/// </summary>
[NotInParallel]
public sealed class PlotAuctionManagerTests
{
    private const uint AliceId = 11;
    private const uint BobId = 12;
    private const uint CarlId = 13;
    private const string AliceName = "Alice";
    private const string BobName = "Bob";
    private const string CarlName = "Carl";

    private const uint ActivityId = 1001;
    private const uint ActiveConfigId = 1;
    private const uint EndedConfigId = 2;
    private const uint LockoutConfigId = 3;
    private const uint BrokenConfigId = 4;

    private const long StartingMoney = 10_000;
    private const uint PrizeItemId = 23490;

    // "28|0|100" and bid_increase_pct 500 — the shipped row's shape: first floor is
    // floor(100 * 1.05) = 105, then floor(105 * 1.05) = 110, then 115.
    private const long FirstFloor = 105;
    private const long SecondFloor = 110;
    private const long ThirdFloor = 115;

    private Mock<IWorldManager> _world;
    private MailManager _mails;
    private NameManager _names;
    private RecordingSaveManager _saves;
    private ItemManager _items;
    private SqliteConnection _content;
    private InMemoryPlotAuctionStore _store;
    private PlotAuctionManager _manager;
    private CharacterMock _alice;
    private CharacterMock _bob;
    private CharacterMock _carl;

    [Before(Test)]
    public void Setup()
    {
        _names = new NameManager();
        _names.Load([], [], []);
        _names.AddCharacter(AliceId, AliceName, 1);
        _names.AddCharacter(BobId, BobName, 2);
        _names.AddCharacter(CarlId, CarlName, 3);

        _mails = new MailManager(
            new SequentialMailIdManager(),
            _names,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
        _mails._allPlayerMails = [];

        _saves = new RecordingSaveManager();
        _world = Mock.Of<IWorldManager>();
        var tasks = new TaskManager(Mock.Of<ITickManager>().Object);

        ResetSingletons();
        var services = new ServiceCollection();
        services.AddSingleton(_mails);
        services.AddSingleton(_names);
        services.AddSingleton(new LocalizationManager());
        services.AddSingleton(tasks);
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _items = BuildItemManager();
        _alice = Character(AliceId, AliceName);
        _bob = Character(BobId, BobName);
        _carl = Character(CarlId, CarlName);
        _world.GetCharacterById(AliceId).Returns(_alice);
        _world.GetCharacterById(BobId).Returns(_bob);
        _world.GetCharacterById(CarlId).Returns(_carl);

        _content = PlotAuctionContentFixture.CreateInMemory();
        var now = DateTime.UtcNow;
        PlotAuctionContentFixture.AddActivity(_content, ActivityId, status: 1);
        PlotAuctionContentFixture.AddConfig(_content, ActiveConfigId, ActivityId,
            "Rare Coastal Plot Auction", "28|0|100", 500, 1, "1|23490|1",
            now.AddHours(-2), now.AddHours(-1), now.AddHours(2));
        PlotAuctionContentFixture.AddConfig(_content, EndedConfigId, ActivityId,
            "Ended Coastal Plot", "28|0|100", 500, 1, "1|23490|1",
            now.AddHours(-4), now.AddHours(-3), now.AddMinutes(-5));
        PlotAuctionContentFixture.AddConfig(_content, LockoutConfigId, ActivityId,
            "Closing Coastal Plot", "28|0|100", 500, 1, "1|23490|1",
            now.AddHours(-2), now.AddHours(-1), now.AddMinutes(10));

        _store = new InMemoryPlotAuctionStore();
        _manager = new PlotAuctionManager(_world.Object, _items);
        _manager.UseStore(_store);
        _manager.LoadContent(_content);
    }

    [After(Test)]
    public void Teardown()
    {
        SingletonContainer.ServiceProvider = null;
        ResetSingletons();
        _content?.Dispose();
        _content = null;
        _manager = null;
        _store = null;
        _items = null;
        _mails = null;
        _names = null;
        _saves = null;
        _world = null;
        _alice = null;
        _bob = null;
        _carl = null;
    }

    [Test]
    public async Task PlaceBid_EscrowsTheChargeAndAnOutbidRefundsTheDisplacedBidderExactlyOnce()
    {
        var first = _manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor);
        await Assert.That(first).IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);
        await Assert.That(_manager.BidsFor(ActiveConfigId).Count).IsEqualTo(1);

        // Bob's higher bid pushes Alice below winner_count 1: her escrow comes back to her
        // wallet, once, and no letter exists for her (she was online).
        var second = _manager.PlaceBid(_bob, ActivityId, ActiveConfigId, SecondFloor);
        await Assert.That(second).IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - SecondFloor);
        await Assert.That(_mails._allPlayerMails).IsEmpty();

        // Carl's bid displaces Bob — Alice must not be refunded a second time.
        var third = _manager.PlaceBid(_carl, ActivityId, ActiveConfigId, ThirdFloor);
        await Assert.That(third).IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney);
        await Assert.That(_mails._allPlayerMails).IsEmpty();

        var rows = _manager.BidsFor(ActiveConfigId);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].CharacterId).IsEqualTo(CarlId);
        await Assert.That(_manager.AuctionFor(ActiveConfigId).BasePrice).IsEqualTo(ThirdFloor);
    }

    [Test]
    public async Task PlaceBid_BelowTheFloorOrUnchanged_IsRefusedWithoutCharging()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor - 1))
            .IsEqualTo(PlotAuctionErrorCodes.BidTooLow);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_manager.BidsFor(ActiveConfigId)).IsEmpty();

        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.BidUnchanged);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);
    }

    [Test]
    public async Task PlaceBid_AfterTheWindowOrForAnUnknownAuction_IsRefusedWithThePinnedCodes()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, EndedConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.AuctionEnded);
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, BrokenConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.NotInAuction);
        await Assert.That(_manager.PlaceBid(_alice, ActivityId + 1, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.NotInAuction);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_mails._allPlayerMails).IsEmpty();
    }

    [Test]
    public async Task Disconnect_MidAuction_KeepsStateAndRefundsTheOfflineBidderByLetterOnce()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);

        // Alice disconnects: the world no longer resolves her character, but her escrow row,
        // the auction row and her charged wallet all stay exactly as they were.
        _world.GetCharacterById(AliceId).Returns((Character)null);
        await Assert.That(_manager.BidsFor(ActiveConfigId).Count).IsEqualTo(1);
        await Assert.That(_manager.AuctionFor(ActiveConfigId)).IsNotNull();
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);

        // Bob's bid displaces the offline Alice: refund rides a letter instead of a wallet.
        await Assert.That(_manager.PlaceBid(_bob, ActivityId, ActiveConfigId, SecondFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);

        var refunds = RefundLetters(AliceId);
        await Assert.That(refunds.Count).IsEqualTo(1);
        await Assert.That(refunds[0].Body.CopperCoins).IsEqualTo((int)FirstFloor);
        await Assert.That(refunds[0].MailType).IsEqualTo(MailType.AucBidFail);

        var rows = _manager.BidsFor(ActiveConfigId);
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].CharacterId).IsEqualTo(BobId);
        await Assert.That(_manager.AuctionFor(ActiveConfigId).BasePrice).IsEqualTo(SecondFloor);
    }

    [Test]
    public async Task Restart_RoundTrip_RestoresTheSameAuctionState()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);
        var before = _manager.AuctionFor(ActiveConfigId);
        var beforeBids = _manager.BidsFor(ActiveConfigId);

        // A restart reads content and store back into a fresh manager.
        var restarted = new PlotAuctionManager(_world.Object, _items);
        restarted.UseStore(_store);
        restarted.LoadContent(_content);
        restarted.LoadFromStore();

        var after = restarted.AuctionFor(ActiveConfigId);
        await Assert.That(after).IsNotNull();
        await Assert.That(after.Id).IsEqualTo(before.Id);
        await Assert.That(after.ActivityId).IsEqualTo(before.ActivityId);
        await Assert.That(after.BasePrice).IsEqualTo(before.BasePrice);
        await Assert.That(after.Settled).IsEqualTo(false);

        var afterBids = restarted.BidsFor(ActiveConfigId);
        await Assert.That(afterBids.Count).IsEqualTo(beforeBids.Count);
        await Assert.That(afterBids[0].CharacterId).IsEqualTo(beforeBids[0].CharacterId);
        await Assert.That(afterBids[0].Amount).IsEqualTo(beforeBids[0].Amount);
        await Assert.That(afterBids[0].BidTimeUtc).IsEqualTo(beforeBids[0].BidTimeUtc);

        var config = restarted.ConfigFor(ActiveConfigId);
        await Assert.That(config).IsNotNull();
        await Assert.That(config.StartPrice.Amount).IsEqualTo(FirstFloor - 5); // "28|0|100"
        await Assert.That(config.WinnerCount).IsEqualTo(1u);

        // The restarted process holds the money the pre-restart process took, and enforces the
        // same floor the persisted base price implies.
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);
        await Assert.That(restarted.PlaceBid(_bob, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.BidTooLow);
        await Assert.That(restarted.PlaceBid(_bob, ActivityId, ActiveConfigId, SecondFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - SecondFloor);
    }

    [Test]
    public async Task Exit_WithBid_RefundsTheEscrowOnceAndTheSecondExitChangesNothing()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);

        var exit = _manager.ExitBid(_alice, ActivityId, ActiveConfigId);
        await Assert.That(exit).IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_manager.BidsFor(ActiveConfigId)).IsEmpty();
        await Assert.That(_manager.AuctionFor(ActiveConfigId).BasePrice).IsEqualTo(0L);
        await Assert.That(_mails._allPlayerMails).IsEmpty();

        var again = _manager.ExitBid(_alice, ActivityId, ActiveConfigId);
        await Assert.That(again).IsEqualTo(PlotAuctionErrorCodes.NotInAuction);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_mails._allPlayerMails).IsEmpty();
    }

    [Test]
    public async Task Exit_InsideTheLast20Minutes_IsRefusedAndTheEscrowStaysHeld()
    {
        // The closing-window guard comes from content (20 minutes here); no literal lives in the manager.
        AAEmu.Game.GameData.ContentConfigGameData.Instance.SetForTest("plot_auction_exit_guard_seconds", 1200);
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, LockoutConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);

        var exit = _manager.ExitBid(_alice, ActivityId, LockoutConfigId);
        await Assert.That(exit).IsEqualTo(PlotAuctionErrorCodes.AuctionEnded);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);
        await Assert.That(_manager.BidsFor(LockoutConfigId).Count).IsEqualTo(1);
        await Assert.That(_mails._allPlayerMails).IsEmpty();
    }

    [Test]
    public async Task Exit_WithoutAGuardRow_IsAllowedNearCloseAndRefundsTheEscrow()
    {
        // A 0 row exercises the same no-guard path as an absent row (the singleton cannot unseed),
        // and the escrow returns exactly once.
        AAEmu.Game.GameData.ContentConfigGameData.Instance.SetForTest("plot_auction_exit_guard_seconds", 0);
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, LockoutConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);

        var exit = _manager.ExitBid(_alice, ActivityId, LockoutConfigId);
        await Assert.That(exit).IsEqualTo(PlotAuctionErrorCodes.Success);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
        await Assert.That(_manager.BidsFor(LockoutConfigId).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Settle_TakesTheWinnerPaymentDeliversThePrizeAndRefundsTheLoserOnce()
    {
        SeedEndedAuction();
        _world.GetCharacterById(CarlId).Returns((Character)null); // loser offline at settlement

        var settled = _manager.TrySettle(EndedConfigId);
        await Assert.That(settled).IsTrue();

        // Winner pays: the held amount stays consumed — charged exactly once across bid and settlement.
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - SecondFloor);

        // Ownership transfers: the winner's letter carries the configured reward item.
        var prizes = MailsOfType(MailType.AucBidWin).Where(m => m.Header.ReceiverId == BobId).ToList();
        await Assert.That(prizes.Count).IsEqualTo(1);
        var prize = prizes[0].Body.Attachments.Single();
        await Assert.That(prize.TemplateId).IsEqualTo(PrizeItemId);
        await Assert.That(prize.OwnerId).IsEqualTo(BobId);
        await Assert.That(prize.SlotType).IsEqualTo(SlotType.Mail);

        // Loser refunds: exactly one letter, for exactly the held amount.
        var refunds = RefundLetters(CarlId);
        await Assert.That(refunds.Count).IsEqualTo(1);
        await Assert.That(refunds[0].Body.CopperCoins).IsEqualTo((int)FirstFloor);
        await Assert.That(_carl.Money).IsEqualTo(StartingMoney - FirstFloor);

        // Escrow consumed, state settled.
        await Assert.That(_manager.BidsFor(EndedConfigId)).IsEmpty();
        await Assert.That(_manager.AuctionFor(EndedConfigId).Settled).IsTrue();
    }

    [Test]
    public async Task Settle_RefusesASecondRunAndDeliversNothingMore()
    {
        SeedEndedAuction();
        await Assert.That(_manager.TrySettle(EndedConfigId)).IsTrue();

        var mailsAfterFirst = _mails._allPlayerMails.Count;
        var bobMoney = _bob.Money;
        var carlMoney = _carl.Money;

        await Assert.That(_manager.TrySettle(EndedConfigId)).IsFalse();
        await Assert.That(_mails._allPlayerMails.Count).IsEqualTo(mailsAfterFirst);
        await Assert.That(_bob.Money).IsEqualTo(bobMoney);
        await Assert.That(_carl.Money).IsEqualTo(carlMoney);
        await Assert.That(_manager.AuctionFor(EndedConfigId).Settled).IsTrue();
    }

    [Test]
    public async Task Settle_BeforeTheWindowCloses_IsRefusedAndKeepsTheEscrow()
    {
        await Assert.That(_manager.PlaceBid(_alice, ActivityId, ActiveConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.Success);

        await Assert.That(_manager.TrySettle(ActiveConfigId)).IsFalse();
        await Assert.That(_manager.AuctionFor(ActiveConfigId).Settled).IsFalse();
        await Assert.That(_manager.BidsFor(ActiveConfigId).Count).IsEqualTo(1);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - FirstFloor);
        await Assert.That(_mails._allPlayerMails).IsEmpty();
    }

    [Test]
    public async Task LoadContent_KeepsOnlyParsableRowsOfActiveActivities()
    {
        // The fixture's fourth config is never added — an unknown id must stay unknown rather
        // than being invented from source.
        await Assert.That(_manager.ConfigFor(BrokenConfigId)).IsNull();
        await Assert.That(_manager.ConfigFor(ActiveConfigId)).IsNotNull();

        // A row whose activity is switched off loads but refuses bids.
        PlotAuctionContentFixture.AddActivity(_content, 2002, status: 0);
        PlotAuctionContentFixture.AddConfig(_content, BrokenConfigId, 2002,
            "Switched Off Plot", "28|0|100", 500, 1, "1|23490|1",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(2));
        _manager.LoadContent(_content);
        await Assert.That(_manager.ConfigFor(BrokenConfigId)).IsNotNull();
        await Assert.That(_manager.PlaceBid(_alice, 2002, BrokenConfigId, FirstFloor))
            .IsEqualTo(PlotAuctionErrorCodes.NotInAuction);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Seeds the ended auction with its escrow already charged, exactly the state a
    /// restart would reload: Bob leads with 110 (winner_count 1), Carl holds 105 as the loser.</summary>
    private void SeedEndedAuction()
    {
        _manager.ImportStateForTest(
            new PlotAuction { Id = EndedConfigId, ActivityId = ActivityId, BasePrice = SecondFloor },
            new PlotAuctionBid
            {
                AuctionId = EndedConfigId, CharacterId = BobId, Amount = SecondFloor,
                BidTimeUtc = DateTime.UtcNow.AddMinutes(-30),
            },
            new PlotAuctionBid
            {
                AuctionId = EndedConfigId, CharacterId = CarlId, Amount = FirstFloor,
                BidTimeUtc = DateTime.UtcNow.AddMinutes(-20),
            });
        _bob.Money = StartingMoney - SecondFloor;
        _carl.Money = StartingMoney - FirstFloor;
    }

    private ItemManager BuildItemManager()
    {
        var itemIds = Mock.Of<IItemIdManager>();
        uint nextItemId = 100;
        itemIds.GetNextId().Returns(() => nextItemId++);
        var items = new ItemManager(
            Mock.Of<ISkillManager>().Object,
            itemIds.Object,
            Mock.Of<IContainerIdManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object);
        SetPrivateField(items, "_templates", new Dictionary<uint, ItemTemplate>
        {
            [PrizeItemId] = new ItemTemplate { Id = PrizeItemId, MaxCount = 1 },
        });
        SetPrivateField(items, "_allItems", new Dictionary<ulong, Item>());
        SetPrivateField(items, "_removedItems", new List<ulong>());
        return items;
    }

    private static CharacterMock Character(uint id, string name) =>
        new() { Id = id, Name = name, Money = StartingMoney };

    private List<BaseMail> MailsOfType(MailType type) =>
        _mails._allPlayerMails.Values.Where(m => m.MailType == type).ToList();

    private List<BaseMail> RefundLetters(uint receiverId) =>
        MailsOfType(MailType.AucBidFail).Where(m => m.Header.ReceiverId == receiverId).ToList();

    private static void ResetSingletons()
    {
        foreach (var type in new[]
                 {
                     typeof(Singleton<MailManager>),
                     typeof(Singleton<NameManager>),
                     typeof(Singleton<LocalizationManager>),
                     typeof(Singleton<WorldManager>),
                     typeof(Singleton<TaskManager>),
                     typeof(Singleton<PlotAuctionManager>),
                 })
        {
            type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        }
    }

    private static void SetPrivateField(object instance, string field, object value) =>
        instance.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
}
