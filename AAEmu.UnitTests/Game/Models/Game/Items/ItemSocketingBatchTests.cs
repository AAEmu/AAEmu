using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

[NotInParallel]
public class ItemSocketingBatchTests
{
    [Test]
    [Arguments(10000L, (short)5000, false, 3, 7, 9940L, (short)3500)]
    [Arguments(50L, (short)5000, false, 0, 10, 50L, (short)5000)]
    [Arguments(10000L, (short)1000, false, 0, 10, 10000L, (short)1000)]
    [Arguments(10000L, (short)5000, true, 1, 8, 9970L, (short)4000)]
    public async Task OneCast_InstallsTheBatchOrRejectsItBeforeSpending(
        long money, short labor, bool failSecond, int expectedSockets, int expectedGems,
        long expectedMoney, short expectedLabor)
    {
        using var fixture = new Fixture(money, labor, failSecond);
        var effect = new SkillEffect
        {
            Template = new SpecialEffect
            {
                SpecialEffectTypeId = SpecialType.ItemSocketing,
                Value1 = ItemSocketingRules.ModeInstall
            },
            StartLevel = 1,
            EndLevel = 1,
            Chance = 100,
            ApplicationMethod = SkillEffectApplicationMethod.Target,
            ConsumeSourceItem = true,
            ConsumeItemCount = 1
        };
        var skill = new Skill(new SkillTemplate { Id = 42, ConsumeLaborPower = 500, Effects = [effect] })
        {
            Level = 1
        };
        skill.ApplyEffects(fixture.Owner, new SkillItem(0, fixture.Gem.Id, fixture.Gem.TemplateId),
            fixture.Owner, new SkillCastItemTarget { ObjId = fixture.Owner.ObjId, Id = fixture.Gear.Id },
            new SkillObjectExtraValues { Values = [3 << 8, 0] });

        await Assert.That(fixture.Gear.UsedRndAttrGroupIds.Any()).IsFalse();
        await Assert.That(fixture.Gear.GemIds.Count(id => id != 0)).IsEqualTo(expectedSockets);
        await Assert.That(fixture.Gem.Count).IsEqualTo(expectedGems);
        await Assert.That(fixture.Owner.Money).IsEqualTo(expectedMoney);
        await Assert.That(fixture.Owner.LaborPower).IsEqualTo(expectedLabor);
        await Assert.That(skill.Cancelled).IsEqualTo(expectedGems == 10);
        if (!skill.Cancelled)
        {
            await Assert.That(skill.SourceItemConsumptionHandled).IsTrue();
            await Assert.That(skill.TryConsumeLabor(fixture.Owner)).IsTrue();
            await Assert.That(fixture.Owner.LaborPower).IsEqualTo(expectedLabor);
        }
    }

    private sealed class LaborCharacter() : Character(new UnitCustomModelParams())
    {
        public override void ChangeLabor(short change, int actabilityId) => SetLabor((short)(LaborPower + change));
        public void SetLabor(short value) => Set(this, "_laborPower", value, typeof(Character));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<(FieldInfo Field, object Previous)> _saved = [];
        public LaborCharacter Owner { get; }
        public Item Gem { get; }
        public EquipItem Gear { get; }

        public Fixture(long money, short labor, bool failSecond)
        {
            var items = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
                Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
                Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
            ReplaceSingleton(items);
            ReplaceSingleton(new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));
            ReplaceSingleton(new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object));
            var formulas = new FormulaManager();
            ReplaceSingleton(formulas);
            Set(formulas, "_formulas", new Dictionary<uint, Formula> { [38] = new("10 * (item_used_socket + 1)") });
            Set(items, "_socketNumLimits", new Dictionary<(uint, byte), uint> { [(0, 12)] = 9 });
            Set(items, "_socketLevelLimits", new Dictionary<uint, uint>());
            Set(items, "_gemSocketChanceRow", new Dictionary<uint, uint> { [100] = 1 });
            Set(items, "_socketChanceRows", new Dictionary<uint, uint[]>
            {
                [1] = [0, 10000, failSecond ? 0u : 10000u, 10000, 10000, 10000, 10000, 10000, 10000, 10000]
            });
            Set(items, "_socketChanceFailBreaks", new HashSet<uint>());
            Set(items, "_templates", new Dictionary<uint, ItemTemplate>
            {
                [100] = new ItemTemplate { Id = 100, UseSkillId = 42, MaxCount = 1000 },
                [200] = new EquipItemTemplate { Id = 200, MaxCount = 1 }
            });
            Owner = new LaborCharacter { Id = 71, ObjId = 71, Level = 1, Money = money };
            Owner.SetLabor(labor);
            var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
            typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, Owner);
            var bag = new ItemContainer(71, SlotType.Inventory, false, Owner)
                { Owner = Owner, ContainerId = 501, ContainerSize = 50 };
            Gem = new Item(9001, new ItemTemplate { Id = 100, UseSkillId = 42, MaxCount = 1000 }, 10)
                { OwnerId = 71, SlotType = SlotType.Inventory, Slot = 0, _holdingContainer = bag };
            Gear = new EquipItem(9002, new EquipItemTemplate { Id = 200, MaxCount = 1 }, 1)
                { OwnerId = 71, SlotType = SlotType.Inventory, Slot = 1, Grade = 12, _holdingContainer = bag };
            bag.Items.Add(Gem);
            bag.Items.Add(Gear);
            bag.UpdateFreeSlotCount();
            typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
            typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
                new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
            Owner.Inventory = inventory;
            Set(items, "_allItems", new Dictionary<ulong, Item> { [Gem.Id] = Gem, [Gear.Id] = Gear });
        }

        private void ReplaceSingleton<T>(T instance) where T : class
        {
            var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.NonPublic | BindingFlags.Static)!;
            _saved.Add((field, field.GetValue(null)));
            field.SetValue(null, instance);
        }

        public void Dispose()
        {
            foreach (var (field, previous) in _saved.AsEnumerable().Reverse())
                field.SetValue(null, previous);
        }
    }

    private static void Set(object target, string name, object value, Type type = null) =>
        (type ?? target.GetType()).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
}
