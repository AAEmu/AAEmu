using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

/// <summary>
/// Skill 13499 casts plot 47, one of the 67 plots with no <c>position = 1</c> plot event, so
/// <see cref="Plot.Tree"/> is null. <see cref="Plot.RunAsync"/> used to await that null tree; because every
/// call site starts the plot from <c>Task.Run</c>, the NullReferenceException surfaced only as an
/// unobserved task exception and the skill never ended — no SCPlotEnded, no cooldown, no released TlId, no
/// OnSkillEnd, and both ActivePlotState fields left pointing at the dead plot.
/// </summary>
[NotInParallel]
public class PlotTreelessEndTests
{
    private const uint SkillId = 13499;
    private const uint PlotId = 47;
    private const int CooldownMs = 5000;

    /// <summary>"…free TlId slots remaining with a total of: Gets 12, Releases 7".</summary>
    private static ulong ReleasesReported(string status)
    {
        var marker = status.LastIndexOf("Releases ", StringComparison.Ordinal);
        return marker < 0 ? 0 : ulong.Parse(status[(marker + "Releases ".Length)..]);
    }

    [Test]
    public async Task RunAsync_PlotWithoutTree_EndsTheSkillInsteadOfThrowing()
    {
        var caster = new Unit { ObjId = 100 };
        var skill = new Skill
        {
            Id = SkillId,
            TlId = SkillTlIdManager.GetNextId(caster),
            Template = new SkillTemplate
            {
                Id = SkillId,
                CooldownTime = CooldownMs,
                // 13499 is one of the two casters of a treeless plot that are plot_only, so the plot owns
                // the end of the skill and must run the end sequence itself.
                PlotOnly = true,
                Plot = new Plot { Id = PlotId } // Tree left null, exactly as PlotManager leaves it
            }
        };
        await Assert.That(skill.TlId).IsNotEqualTo((ushort)0);

        var callbackFired = false;
        skill.Callback = () => callbackFired = true;
        var releasesBefore = ReleasesReported(SkillTlIdManager.ReportStatus());

        await skill.Template.Plot.RunAsync(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill);

        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
        await Assert.That(caster.ActivePlotState).IsNull();
        await Assert.That(skill.ActivePlotState).IsNull();
        // The callback and the cooldown are both armed by the plot-end sequence itself, so these two prove
        // the real end path ran rather than the call being a silent no-op.
        await Assert.That(callbackFired).IsTrue();
        await Assert.That(caster.Cooldowns.CheckCooldown(SkillId)).IsTrue();
        await Assert.That(ReleasesReported(SkillTlIdManager.ReportStatus()) - releasesBefore).IsEqualTo(1ul);
    }

    /// <summary>
    /// The other 28 casters of a treeless plot are <c>plot_only = 'f'</c>: Skill.Use starts the plot from
    /// Task.Run (Skill.cs:331) and then casts, fires and ends the skill itself, so the plot must only drop
    /// its state. Running the plot-end sequence here released the TlId mid-cast and armed the cooldown
    /// before the skill had fired.
    /// </summary>
    [Test]
    public async Task RunAsync_PlotWithoutTreeOnACastSkill_OnlyDropsThePlotState()
    {
        const uint castSkillId = 16728; // casts treeless plot 283 and is plot_only 'f'
        const uint castPlotId = 283;

        var caster = new Unit { ObjId = 101 };
        var skill = new Skill
        {
            Id = castSkillId,
            TlId = SkillTlIdManager.GetNextId(caster),
            Template = new SkillTemplate
            {
                Id = castSkillId,
                CooldownTime = CooldownMs,
                PlotOnly = false,
                Plot = new Plot { Id = castPlotId } // Tree left null, exactly as PlotManager leaves it
            }
        };

        var callbackFired = false;
        skill.Callback = () => callbackFired = true;
        var releasesBefore = ReleasesReported(SkillTlIdManager.ReportStatus());

        await skill.Template.Plot.RunAsync(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill);

        // The plot state is gone from both places, but the cast still owns the skill: the TlId is still
        // held, no cooldown is armed and the callback is untouched, because EndSkill has not run yet.
        await Assert.That(caster.ActivePlotState).IsNull();
        await Assert.That(skill.ActivePlotState).IsNull();
        await Assert.That(skill.TlId).IsNotEqualTo((ushort)0);
        await Assert.That(callbackFired).IsFalse();
        await Assert.That(caster.Cooldowns.CheckCooldown(castSkillId)).IsFalse();
        await Assert.That(ReleasesReported(SkillTlIdManager.ReportStatus()) - releasesBefore).IsEqualTo(0ul);
    }
}
