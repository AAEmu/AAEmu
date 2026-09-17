using System.Reflection;

using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The ZWCreateBuff mirror. The zone applies the buff and tells World; that notification used to go
/// straight to <c>Buffs.AddBuff</c>, which is past every refusal <see cref="BuffTemplate.Apply"/> makes and
/// left World holding a second instance of a buff it already had. The observable case is a
/// multiple-stack family: a second trip through <c>AddBuff</c> grows the stack, so a stack that stays at
/// one is the guard doing its work.
/// </summary>
[NotInParallel]
public class ZoneBuffMirrorRulesTests
{
    private const uint StackingBuffId = 918082;

    private SingletonScope<SkillManager> _skills;
    private SingletonScope<BuffGameData> _buffGameData;

    [Before(Test)]
    public void InstallContentLookups()
    {
        WorldIntegration.ZoneAuthority = false;

        var manager = TestManagers.CreateSkillManager();
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [StackingBuffId] = new BuffTemplate { Id = StackingBuffId, MaxStack = 5, StackRule = BuffStackRule.Multiple }
        });
        SetField(manager, "_buffGrants", new Dictionary<uint, BuffGrantSet>());
        SetField(manager, "_buffTags", new Dictionary<uint, List<uint>>());
        SetField(manager, "_buffBreakers", new Dictionary<uint, List<uint>>());
        SetField(manager, "_requiredBuffTags", new Dictionary<uint, List<uint>>());
        SetField(manager, "_buffImmunityTags", new Dictionary<uint, List<uint>>());

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(buffGameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(buffGameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());

        _skills = new SingletonScope<SkillManager>(manager);
        _buffGameData = new SingletonScope<BuffGameData>(buffGameData);
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _buffGameData.Dispose();
        _skills.Dispose();
        WorldIntegration.ZoneAuthority = false;
    }

    [Test]
    public async Task Decide_BuffTheUnitAlreadyHolds_IsNotMirrored()
    {
        var outcome = ZoneBuffMirrorRules.Decide(
            StackingBuffId, alreadyLive: true, requiredBuffId: 0, carriesRequiredBuff: false, missingRequiredTag: 0);

        await Assert.That(outcome).IsEqualTo(ZoneBuffMirrorRules.Outcome.AlreadyLive);
        await Assert.That(ZoneBuffMirrorRules.ShouldApply(
            StackingBuffId, true, 0, false, 0)).IsFalse();
    }

    [Test]
    public async Task Decide_FreshBuffWithNoRequirements_IsMirrored()
    {
        await Assert.That(ZoneBuffMirrorRules.ShouldApply(StackingBuffId, false, 0, false, 0)).IsTrue();
        await Assert.That(ZoneBuffMirrorRules.ShouldApply(StackingBuffId, false, 4627, true, 0)).IsTrue();
    }

    [Test]
    public async Task Decide_RequirementsTheUnitMisses_AreRefused()
    {
        await Assert.That(ZoneBuffMirrorRules.Decide(StackingBuffId, false, 4627, false, 0))
            .IsEqualTo(ZoneBuffMirrorRules.Outcome.MissingRequiredBuff);
        await Assert.That(ZoneBuffMirrorRules.Decide(StackingBuffId, false, 0, false, 831))
            .IsEqualTo(ZoneBuffMirrorRules.Outcome.MissingRequiredTag);
    }

    [Test]
    public async Task Decide_BuffIdZero_IsNeverAlreadyLive()
    {
        // A buff_effects row naming no buffs row resolves to id 0 and must not be read as "held".
        await Assert.That(ZoneBuffMirrorRules.ShouldApply(0, alreadyLive: true, 0, false, 0)).IsTrue();
    }

    [Test]
    public async Task BuffTemplate_ApplyASecondTimeWithTheBuffLive_DoesNotGrowTheStack()
    {
        var character = CreateCharacter();
        var template = SkillManager.Instance.GetBuffTemplate(StackingBuffId);

        ApplyOnce(template, character);
        var live = character.Buffs.GetEffectFromBuffId(StackingBuffId);
        await Assert.That(live).IsNotNull();
        await Assert.That(live.Stack).IsEqualTo(1);

        // Without the guard this would reach Buffs.AddBuff, whose Multiple branch grows the live instance.
        ApplyOnce(template, character);

        await Assert.That(character.Buffs.GetEffectFromBuffId(StackingBuffId).Stack).IsEqualTo(1);

        // …and the family does grow on a straight AddBuff, so the stack of one above is the guard and not a
        // family that cannot grow.
        var other = CreateCharacter(id: 2);
        AddDirectly(template, other);
        AddDirectly(template, other);

        await Assert.That(other.Buffs.GetEffectFromBuffId(StackingBuffId).Stack).IsEqualTo(2);
    }

    /// <summary>The ungated application the mirror used to make: straight into <c>Buffs.AddBuff</c>.</summary>
    private static void AddDirectly(BuffTemplate template, Character owner) =>
        owner.Buffs.AddBuff(new Buff(owner, owner, new SkillCasterUnit(owner.ObjId), template, null, DateTime.UtcNow));

    /// <summary>The shape the ZWCreateBuff mirror applies with: unit as its own caster, no skill.</summary>
    private static void ApplyOnce(BuffTemplate template, Character owner) =>
        template.Apply(
            owner,
            new SkillCasterUnit(owner.ObjId),
            owner,
            new SkillCastUnitTarget(owner.ObjId),
            null,
            new EffectSource(template),
            new SkillObject(),
            DateTime.UtcNow);

    private static Character CreateCharacter(uint id = 1)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = $"Char{id}" };
        // Character.Skills is built by Character.Load, so a fresh one has to make it.
        character.Skills = new CharacterSkills(character);
        return character;
    }

    private static void SetField(object target, string field, object value) =>
        target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
