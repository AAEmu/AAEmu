using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Escrow and bid state through the real <see cref="AuctionManager"/> bid / settle / expire
/// path (AAEmu#1552 review). A buyout delivery is made to fail by occupying the mail id the
/// winner's letter will take: <see cref="MailManager.Send"/> refuses to replace a stored
/// mail, the house reverts the claim, and the refund letter that follows still goes through.
/// </summary>
[NotInParallel]
public sealed class AuctionManagerSettleTests
{
    private const uint SellerId = 1;
    private const uint BobId = 2;
    private const uint AliceId = 3;
    private const string SellerName = "Seller";
    private const string BobName = "Bob";
    private const string AliceName = "Alice";
    private const ulong ItemId = 500;
    private const uint ItemTemplateId = 1234;
    private const uint LotId = 7;
    private const long StartPrice = 100;
    private const long BuyoutPrice = 500;
    private const long StartingMoney = 10_000;

    private AuctionManager _house;
    private Mock<IItemManager> _itemMock;
    private MailManager _mailManager;
    private SequentialMailIdManager _mailIds;
    private RecordingSaveManager _saves;
    private Item _item;
    private Item _slice;
    private CharacterMock _alice;
    private CharacterMock _bob;
    private FeatureSet _previousFeatures;

    [Before(Test)]
    public void Setup()
    {
        var names = new NameManager();
        names.Load([], [], []);
        names.AddCharacter(SellerId, SellerName, 1);
        names.AddCharacter(BobId, BobName, 2);
        names.AddCharacter(AliceId, AliceName, 3);

        _mailIds = new SequentialMailIdManager();
        _mailManager = new MailManager(
            _mailIds,
            names,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
        _mailManager._allPlayerMails = [];

        _item = new Item(0)
        {
            Id = ItemId,
            TemplateId = ItemTemplateId,
            Template = new ItemTemplate(),
            Count = 1,
            OwnerId = SellerId,
            SlotType = SlotType.Auction
        };

        // The stack a partial buyout splits off. Grade 0 matches the listed item.
        _slice = new Item(0) { Id = ItemId + 1, TemplateId = ItemTemplateId, Count = 1, OwnerId = SellerId };

        var items = Mock.Of<IItemManager>();
        items.GetItemByItemId(ItemId).Returns(_item);
        items.Create(ItemTemplateId, 1, (byte)0, true).Returns(_slice);
        _itemMock = items;
        var auctionIds = Mock.Of<IAuctionIdManager>();
        auctionIds.GetNextId().Returns(LotId);

        _house = new AuctionManager(
            items.Object,
            names,
            auctionIds.Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object);

        var world = new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

        _saves = new RecordingSaveManager();

        ResetSingletons();
        var services = new ServiceCollection();
        services.AddSingleton(_mailManager);
        services.AddSingleton(names);
        services.AddSingleton(new LocalizationManager());
        services.AddSingleton(world);
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _alice = new CharacterMock { AccountId = 3, Id = AliceId, Name = AliceName, Money = StartingMoney };
        _bob = new CharacterMock { AccountId = 2, Id = BobId, Name = BobName, Money = StartingMoney };

        _previousFeatures = FeaturesManager.Fsets;
    }

    [After(Test)]
    public void Teardown()
    {
        SetFeatures(_previousFeatures);
        SingletonContainer.ServiceProvider = null;
        ResetSingletons();
        _house = null;
        _itemMock = null;
        _mailManager = null;
        _mailIds = null;
        _saves = null;
        _item = null;
        _slice = null;
        _alice = null;
        _bob = null;
    }

    private void ListItem(int count = 1, int minStack = 1, int maxStack = 1)
    {
        _item.Count = count;
        _house.AddAuctionLot(_house.CreateAuctionLot(
            SellerId, SellerName, _item, StartPrice, BuyoutPrice, AuctionDuration.AuctionDuration48Hours, minStack, maxStack));
    }

    private static void SetFeatures(FeatureSet features)
    {
        typeof(FeaturesManager)
            .GetProperty(nameof(FeaturesManager.Fsets), BindingFlags.Static | BindingFlags.Public)
            ?.SetValue(null, features);
    }

    private static void EnablePartialBuy()
    {
        var features = new FeatureSet();
        features.Set(Feature.auctionPartialBuy, true);
        SetFeatures(features);
    }

    private static Inventory CreateInventory(Character owner)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        var type = typeof(Inventory);
        type.GetProperty(nameof(Inventory.Bag))!.SetValue(
            inventory, new ItemContainer(owner.Id, SlotType.Inventory, false, owner));
        type.GetProperty(nameof(Inventory.MailAttachments))!.SetValue(
            inventory, new ItemContainer(owner.Id, SlotType.Mail, false, owner));
        return inventory;
    }

    [Test]
    public async Task ConcurrentPostAndCancel_DoNotDeadlockOnHouseAndInventoryLocks()
    {
        ListItem();
        var seller = new CharacterMock { AccountId = 1, Id = SellerId, Name = SellerName, Money = StartingMoney };
        seller.Inventory = CreateInventory(seller);
        using var cancelHasHouseLock = new ManualResetEventSlim();
        using var continueCancel = new ManualResetEventSlim();
        using var postFinished = new ManualResetEventSlim();
        _itemMock.GetItemByItemId(ItemId).Returns(() =>
        {
            cancelHasHouseLock.Set();
            continueCancel.Wait(TimeSpan.FromSeconds(5));
            return _item;
        });

        var cancel = Task.Run(() => _house.CancelAuctionLot(seller, LotId));
        var cancelEntered = cancelHasHouseLock.Wait(TimeSpan.FromSeconds(2));
        Exception postError = null;
        var post = new Thread(() =>
        {
            try
            {
                _house.PostLotOnAuction(
                    seller, ulong.MaxValue, StartPrice, BuyoutPrice, AuctionDuration.AuctionDuration48Hours, 1, 1);
            }
            catch (Exception error)
            {
                postError = error;
            }
            finally
            {
                postFinished.Set();
            }
        }) { IsBackground = true };

        var postWaitedForHouse = false;
        var inventoryAvailable = false;
        try
        {
            post.Start();
            postWaitedForHouse = SpinWait.SpinUntil(
                () => (post.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(2));

            // A listing queued behind cancellation must not own the inventory monitor that
            // cancellation needs to return the escrowed item.
            inventoryAvailable = Monitor.TryEnter(seller.Inventory.MutationSyncRoot);
            if (inventoryAvailable)
                Monitor.Exit(seller.Inventory.MutationSyncRoot);
        }
        finally
        {
            // On the old inventory -> house order, interrupting the queued listing releases
            // its inventory lease so this regression test can unwind rather than leak a deadlock.
            if (!inventoryAvailable && postWaitedForHouse)
                post.Interrupt();
            continueCancel.Set();
        }

        var cancelCompleted = await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(2))) == cancel;
        var postCompleted = postFinished.Wait(TimeSpan.FromSeconds(2));
        if (cancelCompleted)
            await cancel;
        await Assert.That(cancelEntered).IsTrue();
        await Assert.That(postWaitedForHouse).IsTrue();
        await Assert.That(inventoryAvailable).IsTrue();
        await Assert.That(cancelCompleted).IsTrue();
        await Assert.That(postCompleted).IsTrue();
        await Assert.That(postError).IsNull();
        await Assert.That(_house.AuctionLots.ContainsKey(LotId)).IsFalse();
        var cancellationMails = MailsOfType(MailType.AucOffCancel);
        await Assert.That(cancellationMails.Count).IsEqualTo(1);
        await Assert.That(cancellationMails[0].Body.Attachments).Contains(_item);
    }

    [Test]
    public async Task FullBuyout_BuyerMailFails_RestoresLotWithoutTheRefundedBid_AndExpiryReturnsItemToSeller()
    {
        ListItem();
        _house.BidOnAuctionLot(_alice, Bid(150));
        var lot = _house.AuctionLots[LotId];
        await Assert.That(lot.BidderId).IsEqualTo(AliceId);
        await Assert.That(_alice.Money).IsEqualTo(StartingMoney - 150);

        // Alice's refund letter takes the first id; Bob's win letter would take the second.
        BlockNextMailIdAfter(1);
        _house.BidOnAuctionLot(_bob, Bid(BuyoutPrice));

        // Alice was refunded on the outbid, Bob on the failed delivery. Nobody holds a bid.
        await Assert.That(_house.AuctionLots.ContainsKey(LotId)).IsTrue();
        lot = _house.AuctionLots[LotId];
        await Assert.That(lot.BidderId).IsEqualTo(0u);
        await Assert.That(lot.BidMoney).IsEqualTo(0L);
        await Assert.That(lot.BidderName).IsEqualTo(string.Empty);
        await Assert.That(_item.SlotType).IsEqualTo(SlotType.Auction);
        await Assert.That(_item.OwnerId).IsEqualTo((ulong)SellerId);
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - BuyoutPrice);
        await Assert.That(RefundMailCopper(AliceId)).IsEquivalentTo([150]);
        await Assert.That(RefundMailCopper(BobId)).IsEquivalentTo([(int)BuyoutPrice]);

        lot.EndTime = DateTime.UtcNow.AddSeconds(-1);
        _house.UpdateAuctionHouse();

        await Assert.That(_house.AuctionLots.ContainsKey(LotId)).IsFalse();
        await Assert.That(MailsOfType(MailType.AucBidWin)).IsEmpty();
        var returned = MailsOfType(MailType.AucOffFail);
        await Assert.That(returned.Count).IsEqualTo(1);
        await Assert.That(returned[0].Header.ReceiverId).IsEqualTo(SellerId);
        await Assert.That(returned[0].Body.Attachments).Contains(_item);
        await Assert.That(_item.OwnerId).IsEqualTo((ulong)SellerId);
    }

    [Test]
    public async Task SelfBuyout_BuyerMailFails_RefundsTheWholePriceAndRestoresLotWithoutABid()
    {
        ListItem();
        _house.BidOnAuctionLot(_bob, Bid(150));
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - 150);

        // No outbid letter this time: Bob's win letter is the first mail sent.
        BlockNextMailIdAfter(0);
        _house.BidOnAuctionLot(_bob, Bid(BuyoutPrice));

        var lot = _house.AuctionLots[LotId];
        await Assert.That(lot.BidderId).IsEqualTo(0u);
        await Assert.That(lot.BidMoney).IsEqualTo(0L);
        await Assert.That(_item.SlotType).IsEqualTo(SlotType.Auction);
        // Charged 150 then the 350 difference; the refund letter carries the full buyout.
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - BuyoutPrice);
        await Assert.That(RefundMailCopper(BobId)).IsEquivalentTo([(int)BuyoutPrice]);
    }

    /// <summary>
    /// The standing bidder buys one item of a ten-stack and the letter fails. The whole buyout
    /// is refunded, so the bid that was folded into it must leave the stack too.
    /// </summary>
    [Test]
    public async Task PartialSelfBuyout_BuyerMailFails_KeepsTheStackWithoutTheRefundedBid_AndExpiryReturnsItToSeller()
    {
        EnablePartialBuy();
        ListItem(count: 10, minStack: 1, maxStack: 10);
        _house.BidOnAuctionLot(_bob, Bid(150));
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - 150);

        BlockNextMailIdAfter(0);
        _house.BidOnAuctionLot(_bob, new AuctionBid { LotId = LotId, Money = BuyoutPrice, StackSize = 1 });

        var lot = _house.AuctionLots[LotId];
        await Assert.That(lot.BidderId).IsEqualTo(0u);
        await Assert.That(lot.BidMoney).IsEqualTo(0L);
        await Assert.That(_item.Count).IsEqualTo(10);
        await Assert.That(_item.SlotType).IsEqualTo(SlotType.Auction);
        await Assert.That(_bob.Money).IsEqualTo(StartingMoney - BuyoutPrice);
        await Assert.That(RefundMailCopper(BobId)).IsEquivalentTo([(int)BuyoutPrice]);

        lot.EndTime = DateTime.UtcNow.AddSeconds(-1);
        _house.UpdateAuctionHouse();

        await Assert.That(_house.AuctionLots.ContainsKey(LotId)).IsFalse();
        await Assert.That(MailsOfType(MailType.AucBidWin)).IsEmpty();
        var returned = MailsOfType(MailType.AucOffFail);
        await Assert.That(returned.Count).IsEqualTo(1);
        await Assert.That(returned[0].Header.ReceiverId).IsEqualTo(SellerId);
        await Assert.That(returned[0].Body.Attachments).Contains(_item);
        await Assert.That(_item.Count).IsEqualTo(10);
    }

    /// <summary>
    /// The save an outbid forces must already show the replacement bidder. Saving from inside
    /// the refund letter committed the bid that letter had just returned.
    /// </summary>
    [Test]
    public async Task Outbid_PersistsOnceWithTheReplacementBidStanding()
    {
        ListItem();
        var lot = _house.AuctionLots[LotId];
        var committed = new List<(uint bidder, long bid, int mails, long aliceMoney, long bobMoney)>();
        _saves.OnSave = () => committed.Add(
            (lot.BidderId, lot.BidMoney, _mailManager._allPlayerMails.Count, _alice.Money, _bob.Money));

        _house.BidOnAuctionLot(_alice, Bid(150));
        await Assert.That(_saves.SaveCount).IsEqualTo(1);
        await Assert.That(committed[0]).IsEqualTo((AliceId, 150L, 0, StartingMoney - 150, StartingMoney));

        _house.BidOnAuctionLot(_bob, Bid(200));
        await Assert.That(_saves.SaveCount).IsEqualTo(2);
        await Assert.That(committed[1]).IsEqualTo((BobId, 200L, 1, StartingMoney - 150, StartingMoney - 200));
        await Assert.That(RefundMailCopper(AliceId)).IsEquivalentTo([150]);
    }

    private static AuctionBid Bid(long money) => new() { LotId = LotId, Money = money };

    /// <summary>Occupies the id of the (<paramref name="lettersBefore"/> + 1)-th letter sent from now on.</summary>
    private void BlockNextMailIdAfter(int lettersBefore)
    {
        var blocked = _mailIds.Next + (uint)lettersBefore;
        _mailManager._allPlayerMails[blocked] = new BaseMail { Id = blocked, MailType = MailType.InvalidMailType };
    }

    private List<BaseMail> MailsOfType(MailType type) =>
        _mailManager._allPlayerMails.Values.Where(m => m.MailType == type).ToList();

    private List<int> RefundMailCopper(uint receiverId) =>
        MailsOfType(MailType.AucBidFail)
            .Where(m => m.Header.ReceiverId == receiverId)
            .Select(m => m.Body.CopperCoins)
            .ToList();

    private static void ResetSingletons()
    {
        foreach (var type in new[]
                 {
                     typeof(Singleton<MailManager>),
                     typeof(Singleton<NameManager>),
                     typeof(Singleton<LocalizationManager>),
                     typeof(Singleton<WorldManager>)
                 })
        {
            type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        }
    }
}
