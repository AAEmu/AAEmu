using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Indun;

/// <summary>
/// Pins the mail-attachment rules the official review caught: an uncapped fixed grade must not
/// refuse a reward, an amount larger than the stack size must be split, the item must be held out
/// of world save until the transaction owns it, and the authored strings must be localized.
/// </summary>
public class IndunRewardAttachmentTests
{
    private sealed class StubLocalization : ILocalizationManager
    {
        private readonly Dictionary<(string, string, long), string> _rows;
        public List<(string Table, string Column, long Id, string Fallback)> Lookups { get; } = [];
        public StubLocalization(Dictionary<(string, string, long), string> rows) => _rows = rows;
        public void Load() { }
        public void AddTranslation(string t, string c, long i, string v) => _rows[(t, c, i)] = v;
        public string Get(string t, string c, long i, string fallbackValue = "")
        {
            Lookups.Add((t, c, i, fallbackValue));
            return _rows.TryGetValue((t, c, i), out var value) ? value : fallbackValue;
        }
        public IReadOnlyList<string> GetAll(string t, string c, long i) => [];
    }

    private static ItemTemplate Template(uint id, int maxCount, int fixedGrade) => new()
    {
        Id = id,
        Name = $"item {id}",
        MaxCount = maxCount,
        FixedGrade = fixedGrade,
    };

    private static InstanceReward Reward(uint itemId, int amount) => new(
        Id: 1, InstanceId: 66, InstanceRewardKindId: 6, StartRange: 1, EndRange: 12,
        RewardAmount: amount, UseGameScore: false, RewardTargetId: itemId,
        RewardTargetType: InstanceRewardTargetType.Item, GiveIgnoreVisitedCount: false, ApplyConfig: false);

    /// <summary>An item manager whose template is fixed and whose creates are recorded.</summary>
    private static (IItemManager Manager, List<int> Counts) Items(ItemTemplate template)
    {
        var counts = new List<int>();
        var mock = Mock.Of<IItemManager>();
        mock.GetTemplate(Arg.Any<uint>()).Returns(template);
        mock.Create(Arg.Any<uint>(), Arg.Any<int>(), Arg.Any<byte>(), Arg.Any<bool>())
            .Returns((uint _, int count, byte _, bool _) =>
            {
                counts.Add(count);
                return new Item(0, template, Math.Min(count, byte.MaxValue));
            });
        return (mock.Object, counts);
    }

    private static IndunRewardDeliveryService Service(IItemManager items, ILocalizationManager l10n) =>
        new(() => null!, Mock.Of<IMailManager>().Object, items, null, l10n);

    [Test]
    public async Task AnUncappedFixedGradeDoesNotRefuseTheReward()
    {
        // The shipped catalog carries fixed_grade -1 on most rows, which means uncapped, not invalid.
        var (manager, counts) = Items(Template(500, 100, -1));
        var service = Service(manager, new StubLocalization([]));

        var built = service.BuildAttachments(Reward(500, 7), 42);

        await Assert.That(built.Count).IsEqualTo(1);
        await Assert.That(counts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnAmountLargerThanTheStackSizeIsSplit()
    {
        var (manager, counts) = Items(Template(501, 100, -1));
        var service = Service(manager, new StubLocalization([]));

        var built = service.BuildAttachments(Reward(501, 250), 42);

        await Assert.That(built.Count).IsEqualTo(3);
        await Assert.That(counts[0]).IsEqualTo(100);
        await Assert.That(counts[1]).IsEqualTo(100);
        await Assert.That(counts[2]).IsEqualTo(50);
        await Assert.That(counts.Sum()).IsEqualTo(250);
    }

    [Test]
    public async Task EveryCreatedItemIsHeldOutOfWorldSaveAndAddressedToTheRecipient()
    {
        var (manager, _) = Items(Template(502, 100, -1));
        var service = Service(manager, new StubLocalization([]));

        var built = service.BuildAttachments(Reward(502, 250), 4242);

        await Assert.That(built.Count).IsEqualTo(3);
        foreach (var item in built)
        {
            await Assert.That(item.ExcludeFromWorldSave).IsTrue();
            await Assert.That(item.OwnerId).IsEqualTo(4242ul);
            await Assert.That(item.SlotType).IsEqualTo(SlotType.Mail);
        }
    }

    [Test]
    public async Task AStackSizeOfZeroIsRefusedRatherThanLooping()
    {
        var (manager, _) = Items(Template(503, 0, -1));
        var service = Service(manager, new StubLocalization([]));

        await Assert.That(() => service.BuildAttachments(Reward(503, 5), 1))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LocalizedStringsAreReadFromTheCatalogAndFallBackToTheAuthoredText()
    {
        var l10n = new StubLocalization(new Dictionary<(string, string, long), string>
        {
            [("instance_reward_mail_texts", "mail_sender", 7)] = "Divine Shield",
            [("instance_reward_mail_texts", "mail_title", 7)] = "Instance reward",
        });
        var (manager, _) = Items(Template(504, 100, -1));
        var service = Service(manager, l10n);

        var text = new InstanceRewardMailText(
            7, 66, "신의 방패", "보상", "보상 내용", 1, InstanceRewardMailKind.Basic);
        var mail = InvokeBuildMail(service, text, [Reward(504, 1)]);

        await Assert.That(mail.Header.SenderName).IsEqualTo("Divine Shield");
        await Assert.That(mail.Title).IsEqualTo("Instance reward");
        // No row for mail_body, so the authored text is used rather than an invented one.
        await Assert.That(mail.Body.Text).IsEqualTo("보상 내용");
        await Assert.That(l10n.Lookups.Count(l => l.Table == "instance_reward_mail_texts")).IsEqualTo(3);
    }

    [Test]
    public async Task AuthoredTextIsUsedUnchangedWhenTheCatalogHasNoRowAtAll()
    {
        var (manager, _) = Items(Template(505, 100, -1));
        var service = Service(manager, new StubLocalization([]));

        var text = new InstanceRewardMailText(
            9, 66, "신의 방패", "보상", "보상 내용", 1, InstanceRewardMailKind.Basic);
        var mail = InvokeBuildMail(service, text, [Reward(505, 1)]);

        await Assert.That(mail.Header.SenderName).IsEqualTo("신의 방패");
        await Assert.That(mail.Title).IsEqualTo("보상");
        await Assert.That(mail.Body.Text).IsEqualTo("보상 내용");
    }

    private static BaseMail InvokeBuildMail(
        IndunRewardDeliveryService service,
        InstanceRewardMailText text,
        IReadOnlyList<InstanceReward> rewards)
    {
        var method = typeof(IndunRewardDeliveryService).GetMethod(
            "BuildMail",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (BaseMail)method!.Invoke(
            service,
            [new IndunRewardRecipient(1, "Tester"), text, rewards, new List<Item>()])!;
    }
}