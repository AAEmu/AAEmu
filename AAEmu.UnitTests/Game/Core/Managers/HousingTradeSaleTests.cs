using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils.Mocks;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The townhall trade flow end to end: list rows, listing guards, cancel and purchase
/// settlement. The purchase and cancel paths commit the wallet move, both sale letters and
/// the ownership/listing change through one World snapshot, so a rejected snapshot must roll
/// the whole transfer back (exactly-once across a restart).
/// </summary>
[NotInParallel]
public sealed class HousingTradeSaleTests
{
    private const uint SellerId = 11;
    private const uint BuyerId = 12;
    private const string SellerName = "Seller";
    private const string BuyerName = "Buyer";
    private const uint HouseId = 100;
    private const ushort HouseTl = 7;
    private const uint ZoneKey = 555;
    private const short ZoneGroupId = 17;
    private const int Price = 500;
    private const int StartingMoney = 10_000;

    private HousingManager _manager;
    private MailManager _mails;
    private NameManager _names;
    private RecordingSaveManager _saves;
    private Mock<IWorldManager> _world;
    private Mock<IButlerManager> _butler;
    private Mock<IZoneManager> _zones;
    private CharacterMock _seller;
    private CharacterMock _buyer;
    private House _house;
    private WorldInstance _worldInstance;

    [Before(Test)]
    public void Setup()
    {
        _names = new NameManager();
        _names.AddCharacter(SellerId, SellerName, 1);
        _names.AddCharacter(BuyerId, BuyerName, 2);

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

        ResetSingletons();
        var services = new ServiceCollection();
        services.AddSingleton(_mails);
        services.AddSingleton(_names);
        services.AddSingleton(new LocalizationManager());
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();

        _world = Mock.Of<IWorldManager>();
        _zones = Mock.Of<IZoneManager>();
        _zones.GetZoneByKey(ZoneKey).Returns(new Zone { ZoneKey = ZoneKey, GroupId = (uint)ZoneGroupId });
        _butler = Mock.Of<IButlerManager>();
        _butler.UnbindHouse(HouseId, true).Returns(true);

        _manager = new HousingManager(
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IFactionManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            _world.Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IHousingIdManager>().Object,
            Mock.Of<IHousingTldManager>().Object,
            Mock.Of<IItemManager>().Object,
            _mails,
            _names,
            _zones.Object,
            Mock.Of<IDoodadManager>().Object,
            Mock.Of<IUccManager>().Object,
            _butler.Object,
            Mock.Of<IDominionManager>().Object,
            Mock.Of<IGuildDominionManager>().Object);

        _worldInstance = new WorldInstance(new WorldTemplate { Id = 1, Name = "housing-trade-test" }, 0, true, 1);

        _seller = new CharacterMock
        {
            Id = SellerId, AccountId = 1, Name = SellerName, Money = StartingMoney,
            Faction = new SystemFaction { Id = FactionsEnum.Nuian }
        };
        _buyer = new CharacterMock
        {
            Id = BuyerId, AccountId = 2, Name = BuyerName, Money = StartingMoney,
            Faction = new SystemFaction { Id = FactionsEnum.Nuian }
        };

        _house = MakeHouse(HouseId, HouseTl, ZoneKey, hasTemplate: true);
        _house.OwnerId = SellerId;
        _house.AccountId = 1;
        _house.CoOwnerId = SellerId;
        _house.SellPrice = (uint)Price;
        _house.SellPublic = true;

        SetPrivateField(_manager, "_houses", new Dictionary<uint, House> { [_house.Id] = _house });
        SetPrivateField(_manager, "_housesTl", new Dictionary<ushort, House> { [_house.TlId] = _house });
    }

    [After(Test)]
    public void Teardown()
    {
        SingletonContainer.ServiceProvider = null;
        ResetSingletons();
        _worldInstance?.Dispose();
        _manager = null;
        _mails = null;
        _names = null;
        _saves = null;
        _world = null;
        _butler = null;
        _zones = null;
        _seller = null;
        _buyer = null;
        _house = null;
        _worldInstance = null;
    }

    [Test]
    public async Task TradeList_OnlyListsPublicPricedListingsOfTheZoneGroup()
    {
        var privateListing = MakeHouse(200, 20, ZoneKey, hasTemplate: true);
        privateListing.OwnerId = SellerId;
        privateListing.SellPrice = (uint)Price;
        privateListing.SellPublic = false;

        var otherZone = MakeHouse(300, 30, ZoneKey + 1, hasTemplate: true);
        otherZone.OwnerId = SellerId;
        otherZone.SellPrice = (uint)Price;
        _zones.GetZoneByKey(ZoneKey + 1).Returns(new Zone { ZoneKey = ZoneKey + 1, GroupId = 99 });

        var ownerless = MakeHouse(400, 40, ZoneKey, hasTemplate: true);
        ownerless.OwnerId = 0;
        ownerless.SellPrice = (uint)Price;

        var unlisted = MakeHouse(500, 50, ZoneKey, hasTemplate: true);
        unlisted.OwnerId = SellerId;
        unlisted.SellPrice = 0;

        var houses = new Dictionary<uint, House>
        {
            [_house.Id] = _house,
            [privateListing.Id] = privateListing,
            [otherZone.Id] = otherZone,
            [ownerless.Id] = ownerless,
            [unlisted.Id] = unlisted
        };
        SetPrivateField(_manager, "_houses", houses);

        var rows = _manager.GetTradeListings(ZoneGroupId);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Id).IsEqualTo(HouseId);
    }

    [Test]
    public async Task CancelForSale_UnknownHouse_IsRefusedWithoutThrowing()
    {
        var result = _manager.CancelForSale(9999, true);

        await Assert.That(result).IsFalse();
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task CancelForSale_ListedHouse_ClearsListingAndCommitsOnce()
    {
        var result = _manager.CancelForSale(_house, true);

        await Assert.That(result).IsTrue();
        await Assert.That(_house.SellPrice).IsEqualTo(0u);
        await Assert.That(_house.SellPublic).IsTrue();
        await Assert.That(_saves.SaveCount).IsEqualTo(1);
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Count).IsEqualTo(0);
    }

    [Test]
    public async Task CancelForSale_SnapshotRejected_KeepsTheListing()
    {
        _saves.FailNext = true;

        var result = _manager.CancelForSale(_house, true);

        await Assert.That(result).IsFalse();
        await Assert.That(_house.SellPrice).IsEqualTo((uint)Price);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task BuyHouse_HappyPath_TransfersOwnershipAndMoneyExactlyOnce()
    {
        var result = _manager.BuyHouse(HouseTl, (uint)Price, _buyer);

        await Assert.That(result).IsTrue();
        await Assert.That(_buyer.Money).IsEqualTo(StartingMoney - Price);
        await Assert.That(_house.OwnerId).IsEqualTo(BuyerId);
        await Assert.That(_house.AccountId).IsEqualTo(_buyer.AccountId);
        await Assert.That(_house.SellPrice).IsEqualTo(0u);
        await Assert.That(_saves.SaveCount).IsEqualTo(1);

        // The seller's proceeds ride one HousingSale letter; the listing disappears from the
        // trade list, and zone peers get both the sold and the refreshed state packets.
        await Assert.That(_mails.AllPlayerMails.Values.Any(m =>
            m.MailType == MailType.HousingSale
            && m.Header.ReceiverId == SellerId
            && m.Body.CopperCoins == Price)).IsTrue();
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Count).IsEqualTo(0);
        _butler.UnbindHouse(HouseId, true).WasCalled(Times.Once);
    }

    [Test]
    public async Task BuyHouse_NotEnoughMoney_NeverChargesOrFlushes()
    {
        _buyer.Money = Price - 1;

        var result = _manager.BuyHouse(HouseTl, (uint)Price, _buyer);

        await Assert.That(result).IsFalse();
        await Assert.That(_buyer.Money).IsEqualTo(Price - 1);
        await Assert.That(_house.OwnerId).IsEqualTo(SellerId);
        await Assert.That(_house.SellPrice).IsEqualTo((uint)Price);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_mails.AllPlayerMails.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuyHouse_PriceChanged_RejectsWithoutCharging()
    {
        var result = _manager.BuyHouse(HouseTl, (uint)Price + 1, _buyer);

        await Assert.That(result).IsFalse();
        await Assert.That(_buyer.Money).IsEqualTo(StartingMoney);
        await Assert.That(_house.OwnerId).IsEqualTo(SellerId);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task BuyHouse_ProceedsRecipientMissing_RefusesBeforeAnyCharge()
    {
        _house.OwnerId = 999; // no such character name resolves

        var result = _manager.BuyHouse(HouseTl, (uint)Price, _buyer);

        await Assert.That(result).IsFalse();
        await Assert.That(_buyer.Money).IsEqualTo(StartingMoney);
        await Assert.That(_house.OwnerId).IsEqualTo(999u);
        await Assert.That(_house.SellPrice).IsEqualTo((uint)Price);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_mails.AllPlayerMails.Count).IsEqualTo(0);
    }

    [Test]
    public async Task BuyHouse_SnapshotRejected_RollsBackOwnershipMoneyAndLetters()
    {
        _saves.FailNext = true;

        var result = _manager.BuyHouse(HouseTl, (uint)Price, _buyer);

        await Assert.That(result).IsFalse();
        await Assert.That(_buyer.Money).IsEqualTo(StartingMoney);
        await Assert.That(_house.OwnerId).IsEqualTo(SellerId);
        await Assert.That(_house.AccountId).IsEqualTo(1u);
        await Assert.That(_house.SellPrice).IsEqualTo((uint)Price);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_mails.AllPlayerMails.Count).IsEqualTo(0);
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Count).IsEqualTo(1);
        _butler.UnbindHouse(HouseId, true).WasCalled(Times.Never);
    }

    [Test]
    public async Task CancelForSale_BySomeoneElse_KeepsTheListing()
    {
        var result = _manager.CancelForSale(_house, true, _buyer);

        await Assert.That(result).IsFalse();
        await Assert.That(_house.SellPrice).IsEqualTo((uint)Price);
        await Assert.That(_house.OwnerId).IsEqualTo(SellerId);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Count).IsEqualTo(1);
    }

    [Test]
    public async Task SetForSale_MissingHousingRow_FailsLoudly()
    {
        var noTemplate = MakeHouse(600, 60, ZoneKey, hasTemplate: false);
        noTemplate.OwnerId = SellerId;

        var result = _manager.SetForSale(noTemplate, (uint)Price, 0, null, true);

        await Assert.That(result).IsFalse();
        await Assert.That(noTemplate.SellPrice).IsEqualTo(0u);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
    }

    [Test]
    public async Task SetForSale_UnownedHouse_IsRefusedBeforeAnyMarkerOrCharge()
    {
        var ownerless = MakeHouse(700, 70, ZoneKey, hasTemplate: true);
        ownerless.OwnerId = 0;
        ownerless.SellPrice = 0;

        var result = _manager.SetForSale(ownerless, (uint)Price, 0, null, true);

        await Assert.That(result).IsFalse();
        await Assert.That(ownerless.SellPrice).IsEqualTo(0u);
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(_manager.GetTradeListings(ZoneGroupId).Any(h => h.Id == ownerless.Id)).IsFalse();
    }

    private House MakeHouse(uint id, ushort tl, uint zoneKey, bool hasTemplate)
    {
        var house = new House
        {
            Id = id,
            TlId = tl,
            ObjId = 8000u + id,
            Name = "House" + id,
            TemplateId = 1
        };
        if (hasTemplate)
        {
            house.Template = new HousingTemplate
            {
                Id = 1,
                IsSellable = true,
                Taxation = new Taxation { Id = 1, Tax = 5000, SealCount = 1 },
                HousingSize = new HousingSize { Id = 2, GardenRadius = 10f },
                HousingBindingDoodad = []
            };
            house.AttachedDoodads = [];
            house.CurrentStep = -1;
        }

        house.Transform.Local.SetPosition(100f, 200f, 0f);
        // The public setter announces the zone change to a manager this fixture does not run;
        // the trade flow only ever reads the key back, so seed the field directly.
        house.Transform.GetType().GetField("_zoneId",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(house.Transform, zoneKey);
        typeof(GameObject).GetField("_parentWorld",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(house, _worldInstance);
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(365);
        house.Buffs = Mock.Of<IBuffs>().Object;
        return house;
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }

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
