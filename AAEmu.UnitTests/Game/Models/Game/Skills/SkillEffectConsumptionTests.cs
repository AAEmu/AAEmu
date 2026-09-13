using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

[NotInParallel]
public class SkillEffectConsumptionTests
{
    private const uint GroundGrain = 26744;
    private FieldInfo _skillManagerSingletonField;
    private object _previousSkillManager;

    [Before(Test)]
    public void SetupSkillManager()
    {
        _skillManagerSingletonField = typeof(Singleton<SkillManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousSkillManager = _skillManagerSingletonField.GetValue(null);
        _skillManagerSingletonField.SetValue(null, new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object));
    }

    [After(Test)]
    public void RestoreSkillManager()
    {
        _skillManagerSingletonField.SetValue(null, _previousSkillManager);
    }

    [Test]
    public async Task LambFeed_MissingGroundGrainStopsBeforeInteraction()
    {
        var (skill, interaction) = ApplyLambFeed(EmptyInventory());

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(interaction.ApplyCount).IsEqualTo(0);
    }

    [Test]
    public async Task LambFeed_GroundGrainInBankOnlyStopsBeforeInteraction()
    {
        var (skill, interaction) = ApplyLambFeed(InventoryWith((SlotType.Bank, GroundGrain, 1)));

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(interaction.ApplyCount).IsEqualTo(0);
    }

    [Test]
    public async Task LivestockFeed_QueuesTheVerifiedLambAndPenAmounts()
    {
        // skill_effects 30235: pen skill 26129 uses Ground Grain x3.
        var skill = new Skill();
        var costs = new List<(uint templateId, int amount)>();
        var effects = new[] { LambFeedEffect(), SheepPenFeedEffect() };

        var queued = skill.TryQueueEffectItemConsumption(
            effects,
            itemId => itemId == GroundGrain ? 4 : 0,
            costs);

        await Assert.That(queued).IsTrue();
        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(costs).IsEquivalentTo([(GroundGrain, 4)]);
    }

    [Test]
    public async Task WeightedAlternative_DoesNotRequireTheUnselectedItem()
    {
        const uint unavailableAlternative = 90001;
        const uint selectedAlternative = 90002;
        var effects = new List<(BaseUnit target, SkillEffect effect)>
        {
            (null, LambFeedEffect()),
            (null, new SkillEffect { Weight = 30, ConsumeItemId = unavailableAlternative, ConsumeItemCount = 1 }),
            (null, new SkillEffect { Weight = 70, ConsumeItemId = selectedAlternative, ConsumeItemCount = 1 })
        };
        var skill = new Skill();
        var costs = new List<(uint templateId, int amount)>();

        Skill.RetainSelectedWeightedEffect(effects, 30);
        var queued = skill.TryQueueEffectItemConsumption(
            effects.Select(entry => entry.effect),
            itemId => itemId is GroundGrain or selectedAlternative ? 1 : 0,
            costs);

        await Assert.That(queued).IsTrue();
        await Assert.That(effects.Select(entry => entry.effect.ConsumeItemId))
            .IsEquivalentTo([GroundGrain, selectedAlternative]);
        await Assert.That(costs).IsEquivalentTo([(GroundGrain, 1), (selectedAlternative, 1)]);
    }

    [Test]
    public async Task SourceItemCost_RetainsItsExistingConsumptionPath()
    {
        var skill = new Skill();
        var costs = new List<(uint templateId, int amount)>();
        var effect = new SkillEffect
        {
            ConsumeSourceItem = true,
            ConsumeItemId = 12345,
            ConsumeItemCount = 2
        };

        var queued = skill.TryQueueEffectItemConsumption([effect], _ => 0, costs);

        await Assert.That(queued).IsTrue();
        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(costs).IsEquivalentTo([(12345u, 2)]);
    }

    [Test]
    public async Task ApplyEffects_RejectsConcurrentFarmhandInventoryMutation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var interaction = new BlockingInteractionEffect(entered, release)
        {
            WorldInteraction = WorldInteractionType.Use
        };
        var effect = new SkillEffect
        {
            EffectId = 27303,
            Template = interaction,
            ApplicationMethod = SkillEffectApplicationMethod.Target,
            Chance = 100
        };
        var inventory = EmptyInventory();
        var player = new Character(new UnitCustomModelParams()) { ObjId = 1, Inventory = inventory };
        var target = new Unit { ObjId = 2 };
        var skill = new Skill(new SkillTemplate { Id = 20678, Effects = [effect] });
        var applying = Task.Run(() => skill.ApplyEffects(
            player,
            new SkillCasterUnit(player.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            null));
        var enteredTask = Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5)));
        InventoryMutationLease mutation = null;
        try
        {
            var first = await Task.WhenAny(enteredTask, applying);
            if (ReferenceEquals(first, applying))
                await applying;
            if (!await enteredTask)
                throw new TimeoutException("Skill effect did not enter its inventory mutation guard.");

            var acquired = inventory.TryAcquireFarmhandMutation(out mutation);
            await Assert.That(acquired).IsFalse();
            await Assert.That(mutation).IsNull();
        }
        finally
        {
            mutation?.Dispose();
            release.Set();
            await applying.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task ButlerPaidVigor_DelegatesExactSourceWithoutGenericMutation()
    {
        var (skill, player, caster, source, effect) = ButlerConsumable(
            SpecialType.ButlerProductionCostCharge, value: 500, consumeSourceItem: false);
        var service = Mock.Of<IButlerChargeService>();
        service.ChargePaidProductionCost(player, source.Id, 500, 1)
            .Returns(new ButlerProductionCostChargeResult(true, default, default, default));

        var handled = skill.TryHandleButlerConsumable(
            player, caster, [(player, effect)], service.Object, hasExternalItemRows: false);

        await Assert.That(handled).IsTrue();
        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(source.Count).IsEqualTo(1);
        service.ChargePaidProductionCost(player, source.Id, 500, 1).WasCalled(Times.Once);
        service.AddExperience(Any<Character>(), Any<ulong>(), Any<int>(), Any<int>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ButlerPaidVigor_FullVigorCancelsWithoutMutatingSource()
    {
        var (skill, player, caster, source, effect) = ButlerConsumable(
            SpecialType.ButlerProductionCostCharge, value: 500, consumeSourceItem: false);
        var service = Mock.Of<IButlerChargeService>();
        service.ChargePaidProductionCost(player, source.Id, 500, 1)
            .Returns(new ButlerProductionCostChargeResult(
                false,
                ButlerChargeOperationFailure.None,
                ButlerChargeFailure.ProductionCostCapacityReached,
                default));

        var handled = skill.TryHandleButlerConsumable(
            player, caster, [(player, effect)], service.Object, hasExternalItemRows: false);

        await Assert.That(handled).IsTrue();
        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(source.Count).IsEqualTo(1);
        service.ChargePaidProductionCost(player, source.Id, 500, 1).WasCalled(Times.Once);
    }

    [Test]
    public async Task ButlerConsumable_InvalidExactSourceFailsBeforeService()
    {
        var (skill, player, caster, source, effect) = ButlerConsumable(
            SpecialType.ButlerAddExp, value: 2000, consumeSourceItem: true);
        SetSkillItemId(caster, source.Id + 1);
        var service = Mock.Of<IButlerChargeService>();

        var handled = skill.TryHandleButlerConsumable(
            player, caster, [(player, effect)], service.Object, hasExternalItemRows: false);

        await Assert.That(handled).IsTrue();
        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(source.Count).IsEqualTo(1);
        service.AddExperience(Any<Character>(), Any<ulong>(), Any<int>(), Any<int>()).WasCalled(Times.Never);
        service.ChargePaidProductionCost(Any<Character>(), Any<ulong>(), Any<uint>(), Any<int>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ButlerAddExperience_RequiresTheSourceConsumptionFlag()
    {
        var (skill, player, caster, source, effect) = ButlerConsumable(
            SpecialType.ButlerAddExp, value: 2000, consumeSourceItem: false);
        var service = Mock.Of<IButlerChargeService>();

        var handled = skill.TryHandleButlerConsumable(
            player, caster, [(player, effect)], service.Object, hasExternalItemRows: false);

        await Assert.That(handled).IsTrue();
        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(source.Count).IsEqualTo(1);
        service.AddExperience(Any<Character>(), Any<ulong>(), Any<int>(), Any<int>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task ButlerAddExperience_DelegatesTheAuthoritativeEffectValue()
    {
        var (skill, player, caster, source, effect) = ButlerConsumable(
            SpecialType.ButlerAddExp, value: 2000, consumeSourceItem: true);
        var service = Mock.Of<IButlerChargeService>();
        service.AddExperience(player, source.Id, 2000, 1)
            .Returns(new ButlerExperienceGrantResult(true, default, 100, 2100));

        var handled = skill.TryHandleButlerConsumable(
            player, caster, [(player, effect)], service.Object, hasExternalItemRows: false);

        await Assert.That(handled).IsTrue();
        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(source.Count).IsEqualTo(1);
        service.AddExperience(player, source.Id, 2000, 1).WasCalled(Times.Once);
    }

    private static SkillEffect LambFeedEffect() => new()
    {
        EffectId = 27303,
        Template = new InteractionEffect { WorldInteraction = WorldInteractionType.Use },
        ConsumeItemId = GroundGrain,
        ConsumeItemCount = 1
    };

    private static SkillEffect SheepPenFeedEffect() => new()
    {
        EffectId = 38468,
        Template = new InteractionEffect { WorldInteraction = WorldInteractionType.Use },
        ConsumeItemId = GroundGrain,
        ConsumeItemCount = 3
    };

    private static Inventory EmptyInventory()
        => InventoryWith();

    private static Inventory InventoryWith(params (SlotType slotType, uint templateId, int count)[] items)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        var containers = new Dictionary<SlotType, ItemContainer>();
        foreach (var (slotType, templateId, count) in items)
        {
            if (!containers.TryGetValue(slotType, out var container))
            {
                container = new ItemContainer(0, slotType, false, null);
                containers.Add(slotType, container);
            }

            container.Items.Add(new Item { TemplateId = templateId, Count = count });
        }

        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!
            .SetValue(inventory, containers);
        return inventory;
    }

    private static (Skill skill, CountingInteractionEffect interaction) ApplyLambFeed(Inventory inventory)
    {
        // skill_effects 21549: skill 20678, InteractionEffect 27303, Ground Grain x1.
        var interaction = new CountingInteractionEffect { WorldInteraction = WorldInteractionType.Use };
        var feedEffect = LambFeedEffect();
        feedEffect.Template = interaction;
        feedEffect.ApplicationMethod = SkillEffectApplicationMethod.Target;
        feedEffect.Chance = 100;
        var skill = new Skill(new SkillTemplate { Id = 20678, Effects = [feedEffect] });
        var player = new Character(new UnitCustomModelParams()) { ObjId = 1, Inventory = inventory };
        var target = new Unit { ObjId = 2 };

        skill.ApplyEffects(
            player,
            new SkillCasterUnit(player.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            null);
        return (skill, interaction);
    }

    private static (Skill Skill, Character Player, SkillItem Caster, Item Source, SkillEffect Effect)
        ButlerConsumable(SpecialType specialType, int value, bool consumeSourceItem)
    {
        const uint characterId = 71;
        var itemTemplateId = specialType == SpecialType.ButlerAddExp ? 49617u : 49618u;
        var skillId = specialType == SpecialType.ButlerAddExp ? 45818u : 44725u;
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        var player = new Character(new UnitCustomModelParams()) { Id = characterId, ObjId = characterId };
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, player);
        var bag = new ItemContainer(characterId, SlotType.Inventory, false, player)
        {
            Owner = player,
            ContainerId = 501,
            ContainerSize = 50
        };
        var template = new ItemTemplate
        {
            Id = itemTemplateId,
            MaxCount = 1000,
            UseSkillId = skillId
        };
        var source = new Item(9001, template, 1)
        {
            OwnerId = characterId,
            SlotType = SlotType.Inventory,
            Slot = 4,
            _holdingContainer = bag
        };
        bag.Items.Add(source);
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!
            .SetValue(inventory, new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        player.Inventory = inventory;

        var caster = new SkillItem { ItemTemplateId = itemTemplateId };
        SetSkillItemId(caster, source.Id);
        var effect = new SkillEffect
        {
            Template = new SpecialEffect { SpecialEffectTypeId = specialType, Value1 = value },
            ConsumeSourceItem = consumeSourceItem,
            ConsumeItemCount = 1
        };
        var skill = new Skill(new SkillTemplate { Id = skillId, Effects = [effect] });
        return (skill, player, caster, source, effect);
    }

    private static void SetSkillItemId(SkillItem caster, ulong itemId) =>
        typeof(SkillItem).GetField("_itemId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(caster, itemId);

    private sealed class CountingInteractionEffect : InteractionEffect
    {
        public int ApplyCount { get; private set; }

        public override void Apply(
            BaseUnit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source,
            SkillObject skillObject,
            DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            ApplyCount++;
        }
    }

    private sealed class BlockingInteractionEffect(
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : InteractionEffect
    {
        public override void Apply(
            BaseUnit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source,
            SkillObject skillObject,
            DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            entered.Set();
            release.Wait();
        }
    }
}
