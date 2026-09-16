using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Direct tests for the pure decisions behind a fired buff trigger: which units its effect runs between
/// (<c>buff_triggers.source_agent_id</c> / <c>target_agent_id</c>), what amount it carries
/// (<c>use_damage_amount</c> / <c>use_stack_count</c>) and whether its unit gates allow it at all.
/// </summary>
/// <remarks>
/// Agent ids are the content database's enum_buff_trigger_agents: 0 owner, 1 source, 2 target,
/// 3 original_source.
/// </remarks>
[NotInParallel]
public class BuffTriggerAgentRulesTests
{
    private const uint TagId = 91001;
    private const uint OtherTagId = 91002;
    private const uint TaggedBuffId = 91003;
    private const uint UntaggedBuffId = 91004;

    private SingletonScope<SkillManager> _skills;
    private SingletonScope<BuffGameData> _buffGameData;

    [Before(Test)]
    public void InstallContentLookups()
    {
        _skills = new SingletonScope<SkillManager>(CreateSkillManager());
        _buffGameData = new SingletonScope<BuffGameData>(CreateBuffGameData());
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _buffGameData.Dispose();
        _skills.Dispose();
    }

    #region Agent resolution

    [Test]
    public async Task AbsentAgentColumns_ResolveToOwnerOnBothSides()
    {
        var owner = Unit(1);
        var caster = Unit(2);
        var template = new BuffTriggerTemplate();

        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, caster, new OnDamagedArgs { Attacker = caster });

        await Assert.That(source).IsSameReferenceAs(owner);
        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task UnknownTemplate_ResolvesToOwnerOnBothSides()
    {
        var owner = Unit(1);

        var (source, target) = BuffTriggerAgentRules.Resolve(null, owner, null, EventArgs.Empty);

        await Assert.That(source).IsSameReferenceAs(owner);
        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task SourceAgent_WithoutAnEventUnit_FallsBackToTheCaster()
    {
        // A timeout or dispelled trigger has no attacker; "source" is then the unit that applied the buff.
        var owner = Unit(1);
        var caster = Unit(2);
        var template = new BuffTriggerTemplate { SourceAgentId = BuffTriggerAgent.Source };

        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, caster, new OnTimeoutArgs());

        await Assert.That(source).IsSameReferenceAs(caster);
        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task SourceAgent_WithAnEventUnit_UsesTheUnitThatCausedTheEvent()
    {
        // A debuff cast by the caster on the owner, damaged by a third unit: "source" is the attacker.
        var owner = Unit(1);
        var caster = Unit(2);
        var attacker = Unit(3);
        var template = new BuffTriggerTemplate { SourceAgentId = BuffTriggerAgent.Source };

        var (source, _) = BuffTriggerAgentRules.Resolve(template, owner, caster, new OnDamagedArgs { Attacker = attacker });

        await Assert.That(source).IsSameReferenceAs(attacker);
    }

    [Test]
    public async Task TargetAgent_UsesTheUnitTheEventActedOn()
    {
        // "When I attack, apply to what I attacked" - the victim is only in the event args.
        var owner = Unit(1);
        var victim = Unit(2);
        var template = new BuffTriggerTemplate { TargetAgentId = BuffTriggerAgent.Target };

        var (_, target) = BuffTriggerAgentRules.Resolve(template, owner, owner,
            new OnAttackArgs { Attacker = owner, Target = victim });

        await Assert.That(target).IsSameReferenceAs(victim);
    }

    [Test]
    public async Task TargetAgent_WithoutAnEventUnit_FallsBackToTheOwner()
    {
        var owner = Unit(1);
        var template = new BuffTriggerTemplate { TargetAgentId = BuffTriggerAgent.Target };

        var (_, target) = BuffTriggerAgentRules.Resolve(template, owner, owner, new OnTimeoutArgs());

        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task OriginalSourceAgent_IsTheUnitThatAppliedTheBuff()
    {
        var owner = Unit(1);
        var caster = Unit(2);
        var attacker = Unit(3);
        var template = new BuffTriggerTemplate
        {
            SourceAgentId = BuffTriggerAgent.OriginalSource,
            TargetAgentId = BuffTriggerAgent.OriginalSource
        };

        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, caster,
            new OnDamagedArgs { Attacker = attacker, Amount = 10 });

        await Assert.That(source).IsSameReferenceAs(caster);
        await Assert.That(target).IsSameReferenceAs(caster);
    }

    [Test]
    public async Task UnknownAgentId_DoesNotPointTheEffectAtAnotherUnit()
    {
        var owner = Unit(1);
        var template = new BuffTriggerTemplate
        {
            SourceAgentId = (BuffTriggerAgent)99,
            TargetAgentId = (BuffTriggerAgent)99
        };

        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, Unit(2), EventArgs.Empty);

        await Assert.That(source).IsSameReferenceAs(owner);
        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task NullCasterRequestedAsSource_YieldsNoSourceInsteadOfThrowing()
    {
        // A doodad or item applier leaves Buff.Caster null; a row that asks for it has nothing to apply
        // between, and must not fall back to a unit the row did not name.
        var owner = new BaseUnit { ObjId = 1 };
        var template = new BuffTriggerTemplate { SourceAgentId = BuffTriggerAgent.Source };

        var (source, target) = BuffTriggerAgentRules.Resolve(template, owner, null, new OnTimeoutArgs());

        await Assert.That(source).IsNull();
        await Assert.That(target).IsSameReferenceAs(owner);
    }

    [Test]
    public async Task NonUnitOwner_ResolvesWithoutThrowing()
    {
        var doodad = new BaseUnit { ObjId = 7 };
        var caster = Unit(2);
        var template = new BuffTriggerTemplate { SourceAgentId = BuffTriggerAgent.OriginalSource };

        var (source, target) = BuffTriggerAgentRules.Resolve(template, doodad, caster, EventArgs.Empty);

        await Assert.That(source).IsSameReferenceAs(caster);
        await Assert.That(target).IsSameReferenceAs(doodad);
    }

    #endregion

    #region Amount

    [Test]
    public async Task Amount_IsZeroWhenTheRowAsksForNeither()
    {
        var template = new BuffTriggerTemplate();

        var amount = BuffTriggerAgentRules.ResolveAmount(template, 3, new OnDamagedArgs { Amount = 250 });

        await Assert.That(amount).IsEqualTo(0);
    }

    [Test]
    public async Task Amount_UsesTheDamageWhenTheRowAsksForIt()
    {
        var template = new BuffTriggerTemplate { UseDamageAmount = true };

        await Assert.That(BuffTriggerAgentRules.ResolveAmount(template, 1, new OnDamagedArgs { Amount = 250 })).IsEqualTo(250);
        await Assert.That(BuffTriggerAgentRules.ResolveAmount(template, 1, new OnDamageArgs { Amount = 90 })).IsEqualTo(90);
    }

    [Test]
    public async Task Amount_ScalesWithTheStackCountWhenTheRowAsksForIt()
    {
        // HealEffect reads a triggered fixed heal as value / 1000 * Amount, so one stack is the authored
        // value once (1000), and four stacks is four times it.
        var template = new BuffTriggerTemplate { UseStackCount = true };

        await Assert.That(BuffTriggerAgentRules.ResolveAmount(template, 1, EventArgs.Empty))
            .IsEqualTo(BuffTriggerAgentRules.TriggerFullAmount);
        await Assert.That(BuffTriggerAgentRules.ResolveAmount(template, 4, EventArgs.Empty))
            .IsEqualTo(4 * BuffTriggerAgentRules.TriggerFullAmount);
    }

    [Test]
    public async Task Amount_StackCountNeverReadsBelowOneStack()
    {
        var template = new BuffTriggerTemplate { UseStackCount = true };

        await Assert.That(BuffTriggerAgentRules.ResolveAmount(template, 0, EventArgs.Empty))
            .IsEqualTo(BuffTriggerAgentRules.TriggerFullAmount);
    }

    #endregion

    #region Unit gates

    [Test]
    public async Task Gates_WithoutTagIds_PassEvenWhenTheRowAsksForAny()
    {
        // or_unit_reqs reads as "any of the row's unit requirements", and a row with no tag ids has none.
        var template = new BuffTriggerTemplate { OrUnitReqs = true };

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, Unit(1), Unit(2), Unit(3))).IsTrue();
    }

    [Test]
    public async Task OwnerGate_RequiresTheTagOnTheBuffOwner()
    {
        var owner = Unit(1);
        var template = new BuffTriggerTemplate { OwnerBuffTagId = TagId };

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsFalse();

        AddBuff(owner, TaggedBuffId);
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsTrue();
    }

    [Test]
    public async Task OwnerNoTagGate_IsBlockedWhileTheOwnerCarriesTheTag()
    {
        var owner = Unit(1);
        var template = new BuffTriggerTemplate { OwnerNoBuffTagId = TagId };

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsTrue();

        AddBuff(owner, TaggedBuffId);
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsFalse();
    }

    [Test]
    public async Task TargetGate_ReadsTheEffectTargetAndNotTheOwner()
    {
        var owner = Unit(1);
        var target = Unit(2);
        var template = new BuffTriggerTemplate { TargetBuffTagId = TagId };
        AddBuff(target, TaggedBuffId);

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, target)).IsTrue();

        AddBuff(owner, UntaggedBuffId);
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsFalse();
    }

    [Test]
    public async Task SourceGate_ReadsTheEffectSourceByDefault()
    {
        var owner = Unit(1);
        var source = Unit(2);
        var template = new BuffTriggerTemplate { SourceBuffTagId = TagId };
        AddBuff(source, TaggedBuffId);

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, source, owner)).IsTrue();
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, source)).IsFalse();
    }

    [Test]
    public async Task GateCarriedByOwnerTag_IsCheckedOnTheOwnerEvenWhenTheSourceDiffers()
    {
        var owner = Unit(1);
        var source = Unit(2);
        var template = new BuffTriggerTemplate { SourceNoBuffTagId = TagId };
        AddBuff(owner, TaggedBuffId);

        // Default source unit is clean, so the no-tag gate passes...
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, source, owner)).IsTrue();

        // ...but the row asked for the owner's buffs, where the tag is.
        template.CheckNoTagSrcInOwner = true;
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, source, owner)).IsFalse();
    }

    [Test]
    public async Task OrUnitReqs_PassesWhenAnyGateIsMet()
    {
        var owner = Unit(1);
        var template = new BuffTriggerTemplate { OwnerBuffTagId = TagId, OwnerNoBuffTagId = OtherTagId };

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsFalse();

        template.OrUnitReqs = true;
        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsTrue();
    }

    [Test]
    public async Task Gates_WithBothOwnerGatesMet_Pass()
    {
        var owner = Unit(1);
        AddBuff(owner, TaggedBuffId);
        var template = new BuffTriggerTemplate { OwnerBuffTagId = TagId, SourceNoBuffTagId = OtherTagId };

        await Assert.That(BuffTriggerAgentRules.AllowsGates(template, owner, owner, owner)).IsTrue();
    }

    #endregion

    private static Unit Unit(uint objId) => new() { ObjId = objId };

    /// <summary>Puts a live buff on the unit so the tag lookups have something to find.</summary>
    private static void AddBuff(Unit unit, uint buffId)
    {
        var template = SkillManager.Instance.GetBuffTemplate(buffId);
        unit.Buffs.AddBuff(new Buff(unit, unit, new SkillCasterUnit(unit.ObjId), template, null, DateTime.UtcNow)
        {
            Passive = true,
            AbLevel = 1
        });
    }

    private static SkillManager CreateSkillManager()
    {
        var manager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [TaggedBuffId] = new BuffTemplate { Id = TaggedBuffId, Duration = 0 },
            [UntaggedBuffId] = new BuffTemplate { Id = UntaggedBuffId, Duration = 0 }
        });
        SetField(manager, "_taggedBuffs", new Dictionary<uint, List<uint>> { [TagId] = [TaggedBuffId] });
        return manager;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);

    /// <summary>Buff movement/refresh reads buff_modifiers; with no content loaded they must come back
    /// empty rather than from a null table.</summary>
    private static BuffGameData CreateBuffGameData()
    {
        var gameData = new BuffGameData();
        SetField(gameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        SetField(gameData, "_buffTolerances", new Dictionary<uint, BuffTolerance>());
        SetField(gameData, "_buffTolerancesById", new Dictionary<uint, BuffTolerance>());
        return gameData;
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(AAEmu.Commons.Utils.Singleton<T>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;

        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }
}
