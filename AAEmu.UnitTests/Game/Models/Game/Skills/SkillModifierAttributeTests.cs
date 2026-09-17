using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The <c>skill_modifiers</c> attributes this batch made live: heal (12), area_radius (3), global_cooldown
/// (15) and channeling_time (11), plus the item-owned rows the gear walk now registers.
/// </summary>
/// <remarks>
/// Every one of them keeps the value the call site had when no row is present, which is the exact-equality
/// half of each test below: the rows are content, and a content root without them must not move a number.
/// </remarks>
[NotInParallel]
public class SkillModifierAttributeTests
{
    /// <summary>
    /// An empty SkillManager for the length of one test. The assembly hook installs one, but a sibling test
    /// class that scopes and restores the singleton can leave null behind, and every modifier lookup reads
    /// it for the skill's tags.
    /// </summary>
    private static SingletonScope<SkillManager> EmptySkillManager() =>
        new(TestManagers.CreateSkillManager());

    private SingletonScope<SkillManager> _skillManager;

    [Before(HookType.Test)]
    public void InstallEmptySkillManager() => _skillManager = EmptySkillManager();

    [After(HookType.Test)]
    public void RestoreSkillManager() => _skillManager?.Dispose();

    private static Skill SkillOf(uint id, SkillTemplate template = null) => new()
    {
        Template = template ?? new SkillTemplate { Id = id },
        Level = 1
    };

    private static SkillModifier Row(uint ownerId, SkillAttribute attribute, UnitModifierType type, int value,
        uint skillId = 0, uint tagId = 0) => new()
    {
        Id = 1,
        OwnerId = ownerId,
        OwnerType = "Buff",
        SkillId = skillId,
        TagId = tagId,
        SkillAttribute = attribute,
        UnitModifierType = type,
        Value = value
    };

    [Test]
    public async Task HealRows_ScaleTheHeal()
    {
        // 88 Buff rows, all per-cent: 10534 carries +10.
        var cache = new SkillModifiers();
        cache.AddModifier(Row(1, SkillAttribute.Heal, UnitModifierType.Percent, 10, skillId: 10534));
        var skill = SkillOf(10534);

        // The per-cent arithmetic runs in float (10/100f), hence the tolerance; the no-row case below is the
        // exact-equality pin.
        await Assert.That(cache.ApplyModifiers(skill, SkillAttribute.Heal, 100d)).IsEqualTo(110d).Within(1e-4);

        // No row for this skill at all: exactly the base.
        await Assert.That(cache.ApplyModifiers(SkillOf(99999), SkillAttribute.Heal, 100d)).IsEqualTo(100d);
    }

    [Test]
    public async Task HealRows_ReachTheHealEffect()
    {
        var healer = new Unit { ObjId = 300, Level = 50, Hp = 1_000, MaxHp = 100_000, HDps = 100_000 };
        var target = new Unit { ObjId = 301, Level = 50, Hp = 1_000, MaxHp = 100_000 };
        var skill = SkillOf(10534);
        healer.SkillModifiersCache.AddModifier(Row(1, SkillAttribute.Heal, UnitModifierType.Percent, 10, skillId: 10534));

        Heal(healer, target, skill);

        // The heal dps rating composes to exactly 100 before the +10 per-cent.
        await Assert.That(target.Hp - 1_000).IsEqualTo(110);
    }

    [Test]
    public async Task WithoutTheRow_TheHealIsExactlyWhatItWas()
    {
        var healer = new Unit { ObjId = 300, Level = 50, Hp = 1_000, MaxHp = 100_000, HDps = 100_000 };
        var target = new Unit { ObjId = 301, Level = 50, Hp = 1_000, MaxHp = 100_000 };

        Heal(healer, target, SkillOf(10534));

        await Assert.That(target.Hp - 1_000).IsEqualTo(100);
    }

    [Test]
    public async Task AreaRadiusRows_AddMetresToTheTemplateRadius()
    {
        // 45 rows, all flat metre deltas: 11395 carries +5.
        var cache = new SkillModifiers();
        cache.AddModifier(Row(1, SkillAttribute.AreaRadius, UnitModifierType.Value, 5, skillId: 11395));
        var unit = new Unit { ObjId = 400 };
        unit.SkillModifiersCache.AddModifier(Row(1, SkillAttribute.AreaRadius, UnitModifierType.Value, 5, skillId: 11395));

        var skill = SkillOf(11395, new SkillTemplate { Id = 11395, TargetAreaRadius = 6 });

        await Assert.That(skill.EffectiveTargetAreaRadius(unit)).IsEqualTo(11f);
        await Assert.That(cache.ApplyModifiers(SkillOf(99999, new SkillTemplate { TargetAreaRadius = 6 }),
            SkillAttribute.AreaRadius, 6d)).IsEqualTo(6d);
    }

    [Test]
    public async Task WithoutTheRow_TheAreaRadiusIsTheTemplateValue()
    {
        var unit = new Unit { ObjId = 400 };
        var skill = SkillOf(11395, new SkillTemplate { Id = 11395, TargetAreaRadius = 6 });

        await Assert.That(skill.EffectiveTargetAreaRadius(unit)).IsEqualTo(6f);
    }

    [Test]
    public async Task GlobalCooldownRows_ScaleTheArmedCooldown()
    {
        // 11 rows, all per-cent: 18125 carries -3, and the item rows carry -10 and -50.
        var unit = new Unit { ObjId = 500 };
        var skill = SkillOf(18125);
        unit.SkillModifiersCache.AddModifier(Row(1, SkillAttribute.GlobalCooldown, UnitModifierType.Percent, -10, skillId: 18125));

        await Assert.That(skill.GlobalCooldownFactor(unit)).IsEqualTo(0.9f);

        var untouched = new Unit { ObjId = 501 };
        await Assert.That(SkillOf(1).GlobalCooldownFactor(untouched)).IsEqualTo(1f);
    }

    [Test]
    public async Task ChannelingTimeRows_AddMillisecondsToTheTemplateValue()
    {
        // 2 rows, both flat: 10201 carries +2000, 10714 +4000.
        var unit = new Unit { ObjId = 600 };
        unit.SkillModifiersCache.AddModifier(Row(1, SkillAttribute.ChannelingTime, UnitModifierType.Value, 2000, skillId: 10201));

        var skill = SkillOf(10201, new SkillTemplate { Id = 10201, ChannelingTime = 3000 });
        await Assert.That(skill.EffectiveChannelingTime(unit)).IsEqualTo(5000);

        var untouched = new Unit { ObjId = 601 };
        await Assert.That(SkillOf(10201, new SkillTemplate { Id = 10201, ChannelingTime = 3000 })
            .EffectiveChannelingTime(untouched)).IsEqualTo(3000);
    }

    [Test]
    public async Task ItemOwnedRows_ApplyOnlyWhileTheItemIsRegistered()
    {
        // 172 skill_modifiers rows are owned by an item. They reach a unit through the gear walk, which
        // registers the equipped item's rows and takes them back when the loadout changes.
        using var manager = InstallSkillManager();
        var unit = new Unit { ObjId = 700 };
        var skill = SkillOf(18131);

        await Assert.That(unit.SkillModifiersCache.ApplyModifiers(skill, SkillAttribute.Damage, 100d)).IsEqualTo(100d);

        unit.SkillModifiersCache.AddItemModifiers(38123);
        await Assert.That(unit.SkillModifiersCache.ApplyModifiers(skill, SkillAttribute.Damage, 100d)).IsEqualTo(101d);

        unit.SkillModifiersCache.RemoveItemModifiers();
        await Assert.That(unit.SkillModifiersCache.ApplyModifiers(skill, SkillAttribute.Damage, 100d)).IsEqualTo(100d);
    }

    private static void Heal(Unit healer, Unit target, Skill skill)
    {
        var effect = new HealEffect { Id = 1, DpsMultiplier = 1f };

        effect.Apply(
            healer,
            new SkillCasterUnit(healer.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1),
            new EffectSource(skill),
            null,
            DateTime.UtcNow);
    }

    /// <summary>
    /// A <see cref="SkillManager"/> carrying one item-owned skill_modifiers row (the shape of item 38123,
    /// which grants +1 damage to skills 18131/18132/18134), put back on dispose.
    /// </summary>
    private static SingletonScope<SkillManager> InstallSkillManager()
    {
        var manager = TestManagers.CreateSkillManager();
        SetField(manager, "_itemSkillModifiers", new Dictionary<uint, List<SkillModifier>>
        {
            [38123] =
            [
                new SkillModifier
                {
                    Id = 1083,
                    OwnerId = 38123,
                    OwnerType = "Item",
                    SkillId = 18131,
                    SkillAttribute = SkillAttribute.Damage,
                    UnitModifierType = UnitModifierType.Value,
                    Value = 1
                }
            ]
        });

        return new SingletonScope<SkillManager>(manager);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"No field {name} on {target.GetType().Name}");
    }
}

