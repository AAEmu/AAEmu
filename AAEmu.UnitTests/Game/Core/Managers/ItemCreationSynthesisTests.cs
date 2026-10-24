using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class ItemCreationSynthesisTests
{
    [Test]
    [Arguments((byte)2, -1, 1, false)]
    [Arguments((byte)8, -1, 3, false)]
    [Arguments((byte)12, -1, 5, false)]
    [Arguments((byte)12, 2, 1, false)]
    [Arguments((byte)12, -1, 5, true)]
    public async Task ExplicitGrantInitialization_UsesTheFinalGrade_AndPersistsEffects(
        byte requestedGrade, int fixedGrade, int expectedEffects, bool generic)
    {
        var data = ItemEnchantGameData.Instance;
        var saved = new Dictionary<FieldInfo, object>();
        void Seed(string name, object value)
        {
            var field = typeof(ItemEnchantGameData).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            saved[field] = field.GetValue(data)!;
            field.SetValue(data, value);
        }

        try
        {
            Seed("_rndAttrProperties", new Dictionary<(uint, byte), ItemRndAttrCategoryProperty>
            {
                [(7, 2)] = new() { MaxUnitModifierNum = 1 },
                [(7, 8)] = new() { MaxUnitModifierNum = 3 },
                [(7, 12)] = new() { MaxUnitModifierNum = 5 }
            });
            Seed("_rndAttrGroupSets", new Dictionary<uint, List<ItemRndAttrUnitModifierGroupSet>>
            {
                [7] = [new() { Id = 10, PickNum = 1 }, new() { Id = 20, PickNum = 4 }]
            });
            Seed("_rndAttrGroups", new Dictionary<uint, List<ItemRndAttrUnitModifierGroup>>
            {
                [10] = [new() { Id = 101, UnitAttributeId = 1, Weight = 1 },
                        new() { Id = 102, UnitAttributeId = 2, Weight = 1 }],
                [20] = [new() { Id = 201, UnitAttributeId = 3, Weight = 1 },
                        new() { Id = 202, UnitAttributeId = 4, Weight = 1 },
                        new() { Id = 203, UnitAttributeId = 5, Weight = 1 },
                        new() { Id = 204, UnitAttributeId = 6, Weight = 1 }]
            });
            Seed("_rndAttrModifiers", new Dictionary<uint, List<ItemRndAttrUnitModifierRange>>());

            var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
                Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
                Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
            var template = new EquipItemTemplate { Id = 100, FixedGrade = fixedGrade, RndAttrCategoryId = 7 };
            typeof(ItemManager).GetField("_templates", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(manager, new Dictionary<uint, ItemTemplate> { [100] = template });

            var item = generic
                ? manager.Create<EquipItem>(100, 1, requestedGrade, false)
                : (EquipItem)manager.Create(100, 1, requestedGrade, false);

            await Assert.That(item.Grade).IsEqualTo(fixedGrade >= 0 ? (byte)fixedGrade : requestedGrade);
            await Assert.That(item.UsedRndAttrGroupIds.Any()).IsFalse();

            ItemManager.InitializeGrantedSynthesisAttributes(item);

            await Assert.That(item.UsedRndAttrGroupIds.Count()).IsEqualTo(expectedEffects);
            await Assert.That(item.UsedRndAttrGroupIds.Count(id => id is 101 or 102)).IsEqualTo(1);
            await Assert.That(item.EvolvingExp).IsEqualTo(0u);
            await Assert.That(item.EnchantDisabled).IsFalse();

            var stream = new PacketStream();
            item.WriteDetails(stream);
            stream.Rollback();
            var restored = new EquipItem();
            restored.ReadDetails(stream);
            await Assert.That(restored.RndAttrGroupIds).IsEquivalentTo(item.RndAttrGroupIds);
            await Assert.That(stream.LeftBytes).IsEqualTo(0);
        }
        finally
        {
            foreach (var (field, value) in saved)
                field.SetValue(data, value);
        }
    }

    [Test]
    public async Task OrdinaryFactoryCreation_LeavesSynthesisSlotsEmpty()
    {
        var data = ItemEnchantGameData.Instance;
        var saved = new Dictionary<FieldInfo, object>();
        void Seed(string name, object value)
        {
            var field = typeof(ItemEnchantGameData).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
            saved[field] = field.GetValue(data)!;
            field.SetValue(data, value);
        }

        try
        {
            Seed("_rndAttrProperties", new Dictionary<(uint, byte), ItemRndAttrCategoryProperty>
            {
                [(7, 12)] = new() { MaxUnitModifierNum = 5 }
            });
            Seed("_rndAttrGroupSets", new Dictionary<uint, List<ItemRndAttrUnitModifierGroupSet>>
            {
                [7] = [new() { Id = 20, PickNum = 5 }]
            });
            Seed("_rndAttrGroups", new Dictionary<uint, List<ItemRndAttrUnitModifierGroup>>
            {
                [20] = [new() { Id = 201, UnitAttributeId = 3, Weight = 1 }]
            });
            Seed("_rndAttrModifiers", new Dictionary<uint, List<ItemRndAttrUnitModifierRange>>());

            var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
                Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
                Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
            typeof(ItemManager).GetField("_templates", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(manager, new Dictionary<uint, ItemTemplate>
                {
                    [100] = new EquipItemTemplate { Id = 100, FixedGrade = -1, RndAttrCategoryId = 7 }
                });

            var item = manager.Create<EquipItem>(100, 1, 12, false);

            await Assert.That(item.UsedRndAttrGroupIds.Any()).IsFalse();
        }
        finally
        {
            foreach (var (field, value) in saved)
                field.SetValue(data, value);
        }
    }

    [Test]
    public async Task Factory_InitializesDurabilityAfterGrade_WithoutAddingEffectsToOrdinaryEquipment()
    {
        var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
        typeof(ItemManager).GetField("_templates", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, new Dictionary<uint, ItemTemplate>
            {
                [100] = new EquipItemTemplate { Id = 100, FixedGrade = -1 }
            });

        var item = manager.Create<GradedDurabilityEquipment>(100, 1, 12, false);

        await Assert.That(item.Durability).IsEqualTo((byte)22);
        await Assert.That(item.UsedRndAttrGroupIds.Any()).IsFalse();
    }

    public sealed class GradedDurabilityEquipment : EquipItem
    {
        public override byte MaxDurability => (byte)(Grade + 10);

        public GradedDurabilityEquipment(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
            Durability = MaxDurability;
        }
    }
}
