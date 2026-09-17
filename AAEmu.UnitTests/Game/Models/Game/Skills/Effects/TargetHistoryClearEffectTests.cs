using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// plot_effects.actual_type 'TargetHistoryClearEffect' — 8 rows, all at position 1 of 발사 실패 ("firing
/// failed") or 대상 초기화 ("target reset") events, and all of them resolving to nothing before the class and
/// its loader entry existed: <c>PlotEventEffect.ApplyToResolvedTarget</c> returns early on a null template,
/// so the failure branch never cleared anything and the retry could not hit a unit it had already hit.
/// </summary>
public class TargetHistoryClearEffectTests
{
    private static (PlotState State, Unit Hit, uint EventId) BuildPlotHit()
    {
        var caster = new Unit { ObjId = 700 };
        var victim = new Unit { ObjId = 701 };
        var skill = new Skill { Id = 42152, Template = new SkillTemplate { Id = 42152 } };
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), victim,
            new SkillCastUnitTarget(victim.ObjId), new SkillObject(), skill);
        skill.ActivePlotState = state;
        caster.ActivePlotState = state;

        const uint eventId = 40248; // plot 4475's 발사 실패 node owns actual_id 7 of this effect
        state.HitObjects[eventId] = [victim];
        return (state, victim, eventId);
    }

    /// <summary>The exact predicate PlotTargetInfo.FilterTargets applies for a hit_once search.</summary>
    private static bool IsStillFiltered(PlotState state, uint eventId, Unit unit) =>
        state.HitObjects.TryGetValue(eventId, out var hit) && hit.Contains(unit);

    private static void ApplyEffect(PlotState state, BaseUnit caster)
    {
        var effect = new TargetHistoryClearEffect { Id = 7 };
        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId),
            new CastPlot(4475, state.ActiveSkill.TlId, 40248, state.ActiveSkill.Template.Id),
            new EffectSource(state.ActiveSkill), state.SkillObject, DateTime.UtcNow);
    }

    [Test]
    public async Task Clear_LetsAHitOnceSearchPickTheSameTargetAgain()
    {
        var (state, victim, eventId) = BuildPlotHit();
        await Assert.That(IsStillFiltered(state, eventId, victim)).IsTrue();

        ApplyEffect(state, state.Caster);

        await Assert.That(IsStillFiltered(state, eventId, victim)).IsFalse();
        await Assert.That(state.HitObjects.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_LeavesTheRestOfThePlotStateAlone()
    {
        var (state, _, eventId) = BuildPlotHit();
        state.Tickets[eventId] = 3;
        state.Variables[2] = 11;

        ApplyEffect(state, state.Caster);

        await Assert.That(state.Tickets[eventId]).IsEqualTo(3);
        await Assert.That(state.Variables[2]).IsEqualTo(11);
    }

    [Test]
    public async Task Clear_FallsBackToTheCastersPlotState()
    {
        // PlotEventEffect hands the effect an EffectSource whose Skill is the active skill, but a plot_only
        // combo can leave the skill slot empty and the caster slot set; both are consulted.
        var (state, victim, eventId) = BuildPlotHit();
        var skillWithoutState = new Skill { Id = 1, Template = new SkillTemplate { Id = 1 } };
        var effect = new TargetHistoryClearEffect { Id = 8 };

        effect.Apply(state.Caster, new SkillCasterUnit(state.Caster.ObjId), state.Caster,
            new SkillCastUnitTarget(state.Caster.ObjId), new CastPlot(4475, 0, 40248, 1),
            new EffectSource(skillWithoutState), state.SkillObject, DateTime.UtcNow);

        await Assert.That(IsStillFiltered(state, eventId, victim)).IsFalse();
    }

    [Test]
    public async Task Clear_OutsideAPlot_DoesNothing()
    {
        var caster = new Unit { ObjId = 702 };
        var effect = new TargetHistoryClearEffect { Id = 1 };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId),
            new CastPlot(1, 0, 1, 1), new EffectSource(new Skill { Id = 2, Template = new SkillTemplate { Id = 2 } }),
            new SkillObject(), DateTime.UtcNow);

        await Assert.That(caster.ActivePlotState).IsNull();
    }

    [Test]
    public async Task EffectTypeName_MatchesThePlotEffectRows()
    {
        // plot_effects names the type by string ('TargetHistoryClearEffect'), and SkillManager resolves it
        // through that name as the effect dictionary key.
        await Assert.That(typeof(TargetHistoryClearEffect).Name).IsEqualTo("TargetHistoryClearEffect");
        await Assert.That(typeof(TargetHistoryClearEffect).IsSubclassOf(typeof(EffectTemplate))).IsTrue();
    }
}
