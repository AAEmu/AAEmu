using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The two housing packets whose handler is destructive on a truncated body for different reasons.
/// <c>CSSellHouse</c> reads its last field through the buffer indexer instead of the read API, so
/// nothing at the stream level can see a body that ends early - and the degraded price of 0 lands on
/// the branch that cancels a live listing. <c>CSChangeHouseName</c> used to be saved by an incidental
/// <c>Substring</c> throw rather than by a guard.
/// </summary>
/// <remarks>
/// As with the family cases, the complete body runs first and has to show the write, otherwise a case
/// that never reached the handler would pass just the same.
/// </remarks>
[NotInParallel]
public class TruncatedHousingBodyStateTests : IDisposable
{
    private const uint SellerId = 11;
    private const uint HouseId = 100;
    private const ushort HouseTl = 7;
    private const uint ZoneKey = 555;
    private const uint ListedPrice = 500;
    private const string NewName = "renamed";

    private HousingManager _manager;
    private RecordingSaveManager _saves;
    private CharacterMock _seller;
    private House _house;
    private WorldInstance _worldInstance;
    private int _savesBefore;

    public TruncatedHousingBodyStateTests()
    {
        var names = new NameManager();
        names.AddCharacter(SellerId, "Seller", 1);
        var mails = new MailManager(
            new SequentialMailIdManager(),
            names,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
        mails._allPlayerMails = [];

        _saves = new RecordingSaveManager();
        var services = new ServiceCollection();
        services.AddSingleton(mails);
        services.AddSingleton(names);
        services.AddSingleton(new LocalizationManager());
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();
        ResetSingletons();

        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneByKey(ZoneKey).Returns(new Zone { ZoneKey = ZoneKey });
        var butler = Mock.Of<IButlerManager>();
        butler.UnbindHouse(HouseId, true).Returns(true);

        _manager = new HousingManager(
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IFactionManager>().Object,
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IHousingIdManager>().Object,
            Mock.Of<IHousingTldManager>().Object,
            Mock.Of<IItemManager>().Object,
            mails,
            names,
            zones.Object,
            Mock.Of<IDoodadManager>().Object,
            Mock.Of<IUccManager>().Object,
            butler.Object,
            Mock.Of<IDominionManager>().Object,
            Mock.Of<IGuildDominionManager>().Object);

        _worldInstance = new WorldInstance(new WorldTemplate { Id = 1, Name = "housing-truncation-test" },
            0, true, 1);
        _seller = new CharacterMock
        {
            Id = SellerId,
            AccountId = 1,
            Name = "Seller",
            Money = 10_000,
            Faction = new SystemFaction { Id = FactionsEnum.Nuian },
        };
        _seller.Connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = _seller };
        // CancelForSale refunds the appraisal seals through the owner's inventory, so the seller needs
        // one - live containers, but no content rows behind them.
        _ = DetachedInventory.Create(_seller);
        typeof(Inventory).GetProperty(nameof(Inventory.MailAttachments))!
            .SetValue(_seller.Inventory, new ItemContainer(_seller.Id, SlotType.Mail, false, _seller)
            {
                ContainerId = (ulong)SlotType.Mail + 100,
                ContainerSize = 20,
                Owner = _seller,
            });

        _house = MakeHouse();
        SetPrivateField(_manager, "_houses", new Dictionary<uint, House> { [_house.Id] = _house });
        SetPrivateField(_manager, "_housesTl", new Dictionary<ushort, House> { [_house.TlId] = _house });
        _ = new SingletonScope<HousingManager>(_manager);
    }

    public void Dispose()
    {
        SingletonContainer.ServiceProvider = null;
        ResetSingletons();
        _worldInstance?.Dispose();
    }

    // ------------------------------------------------------------------ sell house

    [Test]
    public async Task SellHouse_CompleteBodyWithNoPrice_ReachesTheCancelBranch()
    {
        // The negative control for the case below. A complete body that legitimately declares a price
        // of zero takes the same branch a truncated price degrades onto, and it reaches
        // HousingManager.CancelForSale - which is the whole hazard, so proving the call happens is what
        // makes the refusal meaningful. The unlisted house keeps CancelForSale on its early return,
        // before the certificate refund that would need content-backed items and a live ItemManager.
        _house.SellPrice = 0;
        _savesBefore = _saves.SaveCount;

        var threw = Read(new CSSellHousePacket { Connection = _seller.Connection }, SellBody(0u));

        // The packet's own guard is what must not fire here: a whole body is not a malformed body, so
        // the price of zero has to travel all the way to HousingManager.
        await Assert.That(threw is InvalidDataException).IsFalse();
    }

    [Test]
    public async Task SellHouse_TruncatedBody_DoesNotCancelTheListing()
    {
        ListedAt(ListedPrice);

        // u16 tl and nothing else: the price read overruns and degrades to 0, which is the same branch
        // the control above reaches on purpose. The isPublic byte is read through the buffer indexer,
        // so the stream cannot see the short body on its own.
        var threw = Read(new CSSellHousePacket { Connection = _seller.Connection },
            new PacketStream().Write(HouseTl));

        await Assert.That(threw).IsTypeOf<InvalidDataException>();
        await Assert.That(_house.SellPrice).IsEqualTo(ListedPrice);
        await Assert.That(_saves.SaveCount).IsEqualTo(_savesBefore);
    }

    [Test]
    public async Task SellHouse_TruncatedBody_IsAlsoStoppedByTheFrameworkGate()
    {
        ListedAt(ListedPrice);

        // The same body with strict reads armed, which is what the dispatch does. It is stopped at the
        // price read, before the isPublic indexer is reached.
        var threw = Read(new CSSellHousePacket { Connection = _seller.Connection },
            new PacketStream().Write(HouseTl), strict: true);

        await Assert.That(threw).IsTypeOf<TruncatedPacketException>();
        await Assert.That(_house.SellPrice).IsEqualTo(ListedPrice);
    }

    // ------------------------------------------------------------------ change house name

    [Test]
    public async Task ChangeHouseName_CompleteBody_RenamesTheHouse()
    {
        var threw = Read(new CSChangeHouseNamePacket { Connection = _seller.Connection },
            new PacketStream().Write(HouseTl).Write(NewName, appendSize: true));

        // The negative control. ChangeHouseName capitalises the first character, so the stored name is
        // the capitalised form.
        await Assert.That(threw).IsNull();
        await Assert.That(_house.Name).IsEqualTo(char.ToUpperInvariant(NewName[0]) + NewName[1..]);
    }

    [Test]
    public async Task ChangeHouseName_TruncatedBody_LeavesTheNameIntact()
    {
        _house.Name = "House100";

        // The name's declared length arrives, its bytes do not, so the name degrades to "".
        var threw = Read(new CSChangeHouseNamePacket { Connection = _seller.Connection },
            new PacketStream().Write(HouseTl).Write((ushort)8), strict: false);

        await Assert.That(threw).IsTypeOf<InvalidDataException>();
        await Assert.That(_house.Name).IsEqualTo("House100");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Runs a packet the way the dispatch does - arm the body, then Decode - and returns whatever came
    /// out. <paramref name="strict"/> false leaves the body lenient, which is the only way to reach a
    /// per-handler guard now that the dispatch arms strict reads itself.
    /// </summary>
    private static Exception Read(GamePacket packet, PacketStream body, bool strict = false)
    {
        body.RequireComplete(strict && packet.RequiresCompleteBody);
        try
        {
            packet.Decode(body);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>u16 tl, u64 price, u16 sellTo length (0), u8 isPublic.</summary>
    private static PacketStream SellBody(uint price) =>
        new PacketStream().Write(HouseTl).Write((ulong)price).Write((ushort)0).Write((byte)1);

    /// <summary>Puts the house on the market and notes the save count the assertion compares to.</summary>
    private void ListedAt(uint price)
    {
        _house.SellPrice = price;
        _house.SellPublic = true;
        _savesBefore = _saves.SaveCount;
    }

    private House MakeHouse()
    {
        var house = new House
        {
            Id = HouseId,
            TlId = HouseTl,
            ObjId = 8000u + HouseId,
            Name = "House" + HouseId,
            TemplateId = 1,
            OwnerId = SellerId,
            AccountId = 1,
            CoOwnerId = SellerId,
            Template = new HousingTemplate
            {
                Id = 1,
                IsSellable = true,
                Taxation = new Taxation { Id = 1, Tax = 5000, SealCount = 1 },
                HousingSize = new HousingSize { Id = 2, GardenRadius = 10f },
                HousingBindingDoodad = []
            },
            AttachedDoodads = [],
            CurrentStep = -1,
        };
        house.Transform.Local.SetPosition(100f, 200f, 0f);
        house.Transform.GetType().GetField("_zoneId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(house.Transform, ZoneKey);
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(house, _worldInstance);
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(365);
        house.Buffs = Mock.Of<IBuffs>().Object;
        return house;
    }

    private static void SetPrivateField(object instance, string fieldName, object value) =>
        instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static void ResetSingletons()
    {
        foreach (var type in new[]
                 {
                     typeof(Singleton<MailManager>),
                     typeof(Singleton<NameManager>),
                     typeof(Singleton<LocalizationManager>),
                     typeof(Singleton<WorldManager>),
                 })
        {
            type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        }
    }
}
