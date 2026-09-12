using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillEffectConsumptionTests
{
    private const uint GroundGrain = 26744;

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
}
