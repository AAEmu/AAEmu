using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Escrow through the real <see cref="CraftOrderManager"/> against the store interface: what a
/// post takes and writes, what comes back when the row cannot be written, and what a lapsed or
/// wiped listing mails back. The store double fails on demand. The MySQL store is one statement
/// per call and is not exercised here.
///
/// Inputs are shipped rows: craft 64 (cost 1, actability_limit 0, skill 14621 with consume_lp 20 in
/// actability group 18, product item 4014), the request sheet item 44539 behind
/// <c>const_item_types.craft_order</c>, formula 58 as stored in <c>formulas</c>, and coupon band 1
/// (36 to 48 hours, ticket 46069). Formula 58 at 0 points makes craft 64's floor 22 copper per run.
/// </summary>
[NotInParallel]
public sealed class CraftOrderManagerEscrowTests
{
    private const uint PosterId = 8;
    private const string PosterName = "Poster";
    private const uint SheetItemId = 44539;
    private const uint CraftId = 64;
    private const uint CraftSkillId = 14621;
    private const int CraftLabor = 20;
    private const uint ActabilityGroupId = 18;
    private const uint ProductItemId = 4014;
    private const uint TicketItemId = 46069;
    private const long StartingMoney = 1_000;
    private const ulong FloorForTwoRuns = 44;

    private const string MinFeeFormula =
        "craft_cost * 2.08 + consume_lp + (craft_cost * (require_actability / ((pc_actability + 10000) + consume_lp))) * 0.01";

    private readonly List<(FieldInfo Field, object Previous)> _swapped = [];
    private FailableStore _store;
    private CraftOrderManager _manager;
    private MailManager _mails;

    [Before(Test)]
    public void Setup()
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
        Set(items, "_templates", new Dictionary<uint, ItemTemplate>
        {
            [SheetItemId] = new CraftOrderSheetTemplate { Id = SheetItemId, MaxCount = 1 },
            [ProductItemId] = new ItemTemplate { Id = ProductItemId, MaxCount = 1 }
        });
        Set(items, "_allItems", new Dictionary<ulong, Item>());
        Set(items, "_removedItems", new List<ulong>());
        items.SetConstItemForTest(CraftOrderContent.SheetItemConstName, SheetItemId);
        Swap(items);

        var crafts = new CraftManager();
        Set(crafts, "_crafts", new Dictionary<uint, Craft> { [CraftId] = Craft64() });
        Swap(crafts);

        var skills = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        Set(skills, "_skills", new Dictionary<uint, SkillTemplate>
        {
            [CraftSkillId] = new SkillTemplate { Id = CraftSkillId, ConsumeLaborPower = CraftLabor }
        });
        Swap(skills);

        var formulas = new FormulaManager();
        Set(formulas, "_formulas", new Dictionary<uint, Formula>
        {
            [(uint)FormulaKind.MinCraftOrderFee] = new Formula(MinFeeFormula)
        });
        Swap(formulas);

        Swap(new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object));

        CraftOrderCouponGameData.Instance.SetForTest([new CraftOrderCoupon(TicketItemId, 5, 36, 48)]);

        _store = new FailableStore();
        _manager = new CraftOrderManager { SkipExpiredMail = true };
        _manager.UseStore(_store);
    }

    [After(Test)]
    public void Teardown()
    {
        SingletonContainer.ServiceProvider = null;
        CraftOrderCouponGameData.Instance.SetForTest(null);
        for (var i = _swapped.Count - 1; i >= 0; i--)
            _swapped[i].Field.SetValue(null, _swapped[i].Previous);
        _swapped.Clear();
        _mails = null;
        _manager = null;
        _store = null;
    }

    [Test]
    public async Task Post_EscrowsTheFeeTakesTheSheetAndWritesTheRow()
    {
        var poster = Poster();
        var sheet = SheetInBag(poster, count: 2);

        _manager.Post(poster, sheet.Id, FloorForTwoRuns);

        await Assert.That(poster.Money).IsEqualTo(StartingMoney - (long)FloorForTwoRuns);
        await Assert.That(poster.Inventory.Bag.Items).IsEmpty();
        var rows = _store.LoadAll();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].OwnerId).IsEqualTo(PosterId);
        await Assert.That(rows[0].CraftId).IsEqualTo(CraftId);
        await Assert.That(rows[0].ItemId).IsEqualTo(ProductItemId);
        await Assert.That(rows[0].Count).IsEqualTo(2u);
        await Assert.That(rows[0].Fee).IsEqualTo(FloorForTwoRuns);
        await Assert.That(rows[0].ActabilityGroupId).IsEqualTo(ActabilityGroupId);
        await Assert.That(_manager.Orders.Single().Id).IsEqualTo(rows[0].Id);
        await Assert.That(_store.LoadFeeStats().Single()).IsEqualTo(new CraftOrderFeeStat(CraftId, 22, 22));
    }

    [Test]
    public async Task Post_RowWriteFailure_ReturnsTheFeeAndTheSheet()
    {
        var poster = Poster();
        var sheet = SheetInBag(poster, count: 2);
        _store.FailInserts = true;

        _manager.Post(poster, sheet.Id, FloorForTwoRuns);

        await Assert.That(poster.Money).IsEqualTo(StartingMoney);
        await Assert.That(_manager.Orders).IsEmpty();
        await Assert.That(_store.LoadAll()).IsEmpty();
        var returned = poster.Inventory.Bag.Items.OfType<CraftOrderSheetItem>().ToList();
        await Assert.That(returned.Count).IsEqualTo(1);
        await Assert.That(returned[0].CraftId).IsEqualTo(CraftId);
        await Assert.That(returned[0].CraftCount).IsEqualTo(2u);
        await Assert.That(returned[0].CraftGrade).IsEqualTo((byte)0);
        await Assert.That(returned[0].ActabilityGroupId).IsEqualTo(ActabilityGroupId);
    }

    [Test]
    public async Task Post_BelowThePerRunFloor_TakesNothing()
    {
        var poster = Poster();
        var sheet = SheetInBag(poster, count: 2);

        _manager.Post(poster, sheet.Id, FloorForTwoRuns - 1);

        await Assert.That(poster.Money).IsEqualTo(StartingMoney);
        await Assert.That(poster.Inventory.Bag.Items).Contains(sheet);
        await Assert.That(_manager.Orders).IsEmpty();
        await Assert.That(_store.LoadAll()).IsEmpty();
    }

    [Test]
    public async Task Post_RefusesAnItemThatIsNotASheet()
    {
        var poster = Poster();
        var plain = new Item(900, new ItemTemplate { Id = ProductItemId, MaxCount = 1 }, 1);
        poster.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, plain);

        _manager.Post(poster, plain.Id, FloorForTwoRuns);

        await Assert.That(poster.Money).IsEqualTo(StartingMoney);
        await Assert.That(poster.Inventory.Bag.Items).Contains(plain);
        await Assert.That(_store.LoadAll()).IsEmpty();
    }

    [Test]
    public async Task Sweep_MailsTheEscrowAndTheSheetBackToTheOwner()
    {
        WithMail();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000);
        _manager.ImportForTest(Order(id: 1, expiresUnix: 900, count: 2, fee: FloorForTwoRuns));

        _manager.SweepExpired(now);

        await Assert.That(_manager.Orders).IsEmpty();
        await Assert.That(_store.LoadAll()).IsEmpty();
        var refund = _mails._allPlayerMails.Values.Single();
        await Assert.That(refund.Header.ReceiverId).IsEqualTo(PosterId);
        await Assert.That(refund.Header.SenderName).IsEqualTo(CraftOrderProcessRules.ExpiredMailSender);
        await Assert.That(refund.Body.CopperCoins).IsEqualTo((int)FloorForTwoRuns);
        var sheet = refund.Body.Attachments.Single() as CraftOrderSheetItem;
        await Assert.That(sheet).IsNotNull();
        await Assert.That(sheet.CraftId).IsEqualTo(CraftId);
        await Assert.That(sheet.CraftCount).IsEqualTo(2u);
        await Assert.That(sheet.ActabilityGroupId).IsEqualTo(ActabilityGroupId);
        await Assert.That(sheet.SlotType).IsEqualTo(SlotType.Mail);
        await Assert.That(sheet.OwnerId).IsEqualTo((ulong)PosterId);
    }

    [Test]
    public async Task Clear_RefundsAnOrderWhoseRowIsAlreadyGone()
    {
        WithMail();
        var order = Order(id: 1, expiresUnix: 1_000_000, count: 1, fee: 100_009);
        _manager.ImportForTest(order);
        // The row left MySQL behind the board's back; the board copy is the only record of the escrow.
        await Assert.That(_store.Delete(order.Id)).IsTrue();

        _manager.Clear();

        await Assert.That(_manager.Orders).IsEmpty();
        var refund = _mails._allPlayerMails.Values.Single();
        await Assert.That(refund.Header.ReceiverId).IsEqualTo(PosterId);
        await Assert.That(refund.Body.CopperCoins).IsEqualTo(100_009);
        await Assert.That(refund.Body.Attachments.OfType<CraftOrderSheetItem>().Single().CraftId).IsEqualTo(CraftId);
    }

    [Test]
    public async Task Clear_KeepsTheBoardWhenTheStoreCannotBeWiped()
    {
        WithMail();
        _manager.ImportForTest(Order(id: 1, expiresUnix: 1_000_000, count: 1, fee: 100_009));
        _store.FailWipe = true;

        _manager.Clear();

        await Assert.That(_manager.Orders.Count).IsEqualTo(1);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);
        await Assert.That(_mails._allPlayerMails).IsEmpty();
    }

    /// <summary>
    /// Registers a real <see cref="MailManager"/> and its neighbours so the refund letters can be
    /// read back, the way the auction settle tests do.
    /// </summary>
    private void WithMail()
    {
        var names = new NameManager();
        names.Load([], [], []);
        names.AddCharacter(PosterId, PosterName, 1);
        _mails = new MailManager(
            new SequentialMailIdManager(),
            names,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);
        _mails._allPlayerMails = [];
        var world = new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        var tasks = new TaskManager(Mock.Of<ITickManager>().Object);

        Swap<MailManager>(null);
        Swap<NameManager>(null);
        Swap<LocalizationManager>(null);
        Swap<WorldManager>(null);
        Swap<TaskManager>(null);

        var services = new ServiceCollection();
        services.AddSingleton(_mails);
        services.AddSingleton(names);
        services.AddSingleton(new LocalizationManager());
        services.AddSingleton(world);
        services.AddSingleton(tasks);
        services.AddSingleton<ISaveManager>(new RecordingSaveManager());
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();
        _manager.SkipExpiredMail = false;
    }

    private static CharacterMock Poster()
    {
        var character = new CharacterMock { Id = PosterId, Name = PosterName, Money = StartingMoney };
        character.Actability = new CharacterActability(character);
        DetachedInventory.Create(character);
        return character;
    }

    private static CraftOrderSheetItem SheetInBag(Character character, uint count)
    {
        var sheet = ItemManager.Instance.Create<CraftOrderSheetItem>(SheetItemId, 1, 0);
        sheet.SetOrder(CraftId, 0, count, ActabilityGroupId);
        character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, sheet);
        return sheet;
    }

    private static Craft Craft64()
    {
        var craft = new Craft
        {
            Id = CraftId,
            Orderable = true,
            Cost = 1,
            ActabilityLimit = 0,
            SkillId = CraftSkillId,
            ActabilityGroupId = ActabilityGroupId
        };
        craft.CraftProducts.Add(new CraftProduct { Id = 60, CraftId = CraftId, ItemId = ProductItemId, Amount = 1 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 43782, Amount = 1 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 8337, Amount = 4 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 8256, Amount = 2 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 27545, Amount = 10 });
        return craft;
    }

    private static CraftOrder Order(ulong id, long expiresUnix, uint count, ulong fee) => new()
    {
        Id = id,
        OwnerId = PosterId,
        OwnerName = PosterName,
        OwnerWorldCharKey = PosterId,
        CraftId = CraftId,
        ItemId = ProductItemId,
        Count = count,
        Fee = fee,
        ActabilityGroupId = ActabilityGroupId,
        PostedUnix = expiresUnix - 172_800,
        ExpiresUnix = expiresUnix
    };

    private void Swap<T>(T instance) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _swapped.Add((field, field.GetValue(null)));
        field.SetValue(null, instance);
    }

    private static void Set(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    /// <summary>An in-memory store whose row write or wipe can be made to fail.</summary>
    private sealed class FailableStore : ICraftOrderStore
    {
        private readonly InMemoryCraftOrderStore _inner = new();

        public bool FailInserts { get; set; }
        public bool FailWipe { get; set; }

        public IReadOnlyList<CraftOrder> LoadAll() => _inner.LoadAll();
        public bool Insert(CraftOrder order) => !FailInserts && _inner.Insert(order);
        public bool Delete(ulong orderId) => _inner.Delete(orderId);
        public bool DeleteAll() => !FailWipe && _inner.DeleteAll();
        public IReadOnlyList<CraftOrderFeeStat> LoadFeeStats() => _inner.LoadFeeStats();
        public bool UpsertFeeStats(CraftOrderFeeStat stat) => _inner.UpsertFeeStats(stat);
    }
}
