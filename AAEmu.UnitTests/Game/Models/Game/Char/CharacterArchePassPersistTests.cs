using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Game.GameData;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

[NotInParallel]
public sealed class CharacterArchePassPersistTests
{
    private const uint TestPassId = 88;
    private const int Price = 100000;

    private RecordingSaveManager _saves;
    private MailManager _mailManager;

    [Before(Test)]
    public void Setup()
    {
        ContentConfigTestSeed.BlessAndArchePass();
        _saves = new RecordingSaveManager();
        var nameManager = new NameManager();
        nameManager.Load([], [], []);
        var mailIdManager = new SequentialMailIdManager();
        _mailManager = new MailManager(
            mailIdManager,
            nameManager,
            Mock.Of<IItemManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ILocalizationManager>().Object);

        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);

        var services = new ServiceCollection();
        services.AddSingleton(_mailManager);
        services.AddSingleton<ISaveManager>(_saves);
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();
        _mailManager._allPlayerMails = [];
    }

    [After(Test)]
    public void Teardown()
    {
        SingletonContainer.ServiceProvider = null;
        typeof(Singleton<MailManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
        _saves = null;
        _mailManager = null;
    }

    [Test]
    public async Task Buy_WorldSave_WritesMoneyAndPassTogether()
    {
        var (state, character) = CreatePaid();
        long savedMoney = -1;
        var savedStatus = ArchePassStatus.Invalid;
        _saves.OnSave = () =>
        {
            savedMoney = character.Money;
            savedStatus = state.StatusOf(TestPassId);
        };

        await Assert.That(state.TryBuy(TestPassId)).IsTrue();
        await Assert.That(_saves.SaveCount).IsEqualTo(1);
        await Assert.That(savedMoney).IsEqualTo(character.Money);
        await Assert.That(savedMoney).IsEqualTo(200000 - Price);
        await Assert.That(savedStatus).IsEqualTo(ArchePassStatus.Owned);
        await Assert.That(state.StatusOf(TestPassId)).IsEqualTo(ArchePassStatus.Owned);
    }

    [Test]
    public async Task Buy_WorldSaveFailed_RestoresMoneyAndPass()
    {
        var (state, character) = CreatePaid();
        _saves.FailNext = true;

        await Assert.That(state.TryBuy(TestPassId)).IsFalse();
        await Assert.That(_saves.SaveCount).IsEqualTo(0);
        await Assert.That(character.Money).IsEqualTo(200000);
        await Assert.That(state.StatusOf(TestPassId)).IsEqualTo(ArchePassStatus.Invalid);
    }

    private static (CharacterArchePass State, Character Owner) CreatePaid()
    {
        ArchePassGameData.Instance.SetForTest(new ArchePassDesc
        {
            Id = TestPassId,
            Name = "paid pass",
            CurrencyId = (uint)ContentCurrencyType.Gold,
            CurrencyValue = Price,
            UpgradeItemId = 54232,
            MaxTier = 2
        });

        var character = new Character(new UnitCustomModelParams())
        {
            Id = 1,
            Name = "PassPersist",
            Money = 200000
        };
        var state = new CharacterArchePass(character);
        return (state, character);
    }
}
