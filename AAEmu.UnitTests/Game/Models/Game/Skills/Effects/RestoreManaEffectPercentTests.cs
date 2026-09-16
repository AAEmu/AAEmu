using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>restore_mana_effects.percent</c> (144 of 261 rows) through the real <see cref="RestoreManaEffect"/>
/// path.
/// </summary>
[NotInParallel]
public class RestoreManaEffectPercentTests
{
    private const int TargetMaxMp = 1_000;
    private const int TargetStartMp = 1;
    private const uint CasterObjId = 300;
    private const uint TargetObjId = 301;

    [Test]
    public async Task Percent_TenPercentRestoresTenPercent()
    {
        var (caster, target) = Create();

        Restore(caster, target, new RestoreManaEffect
        {
            Id = 1,
            UseFixedValue = true,
            FixedMin = 10,
            FixedMax = 10,
            Percent = true
        });

        await Assert.That(target.Mp - TargetStartMp).IsEqualTo(100);
    }

    [Test]
    public async Task Percent_UsesTheTargetsOwnCeiling()
    {
        var (caster, target) = Create();
        target.MaxMp = 4_000;

        Restore(caster, target, new RestoreManaEffect
        {
            Id = 1,
            UseFixedValue = true,
            FixedMin = 10,
            FixedMax = 10,
            Percent = true
        });

        await Assert.That(target.Mp - TargetStartMp).IsEqualTo(400);
    }

    [Test]
    public async Task Percent_RangeRollsInsideTheRange()
    {
        // restore-mana effect 89 authors 10-20.
        var (caster, target) = Create();

        for (var i = 0; i < 200; i++)
        {
            target.Mp = TargetStartMp;
            Restore(caster, target, new RestoreManaEffect
            {
                Id = 89,
                UseFixedValue = true,
                FixedMin = 10,
                FixedMax = 20,
                Percent = true
            });

            var restored = target.Mp - TargetStartMp;
            await Assert.That(restored).IsGreaterThanOrEqualTo(100);
            await Assert.That(restored).IsLessThanOrEqualTo(200);
        }
    }

    [Test]
    public async Task AHundredPercentRow_RefillsTheBar()
    {
        var (caster, target) = Create();

        Restore(caster, target, new RestoreManaEffect
        {
            Id = 9,
            UseFixedValue = true,
            FixedMin = 100,
            FixedMax = 100,
            Percent = true
        });

        await Assert.That(target.Mp).IsEqualTo(TargetMaxMp);
    }

    [Test]
    public async Task WithoutPercent_TheRestoreIsExactlyWhatItWas()
    {
        // The byte-identical pin: no percent column means the fixed composition, unchanged.
        var (caster, target) = Create();

        Restore(caster, target, new RestoreManaEffect
        {
            Id = 2,
            UseFixedValue = true,
            FixedMin = 50,
            FixedMax = 50
        });

        await Assert.That(target.Mp - TargetStartMp).IsEqualTo(50);
    }

    [Test]
    public async Task PercentWithoutFixedValue_IsLeftOnTheAbsolutePath()
    {
        // Every shipped percent row also carries use_fixed_value; one that did not would have no
        // percentage to read, so the composition above stays in charge.
        var (caster, target) = Create();

        Restore(caster, target, new RestoreManaEffect
        {
            Id = 100,
            UseFixedValue = false,
            FixedMin = 10,
            FixedMax = 10,
            Percent = true
        });

        await Assert.That(target.Mp - TargetStartMp).IsEqualTo(0);
    }

    private static (Unit Caster, Unit Target) Create() =>
        (new Unit { ObjId = CasterObjId, Level = 50 },
         new Unit { ObjId = TargetObjId, Level = 50, Hp = 100, Mp = TargetStartMp, MaxMp = TargetMaxMp });

    private static void Restore(Unit caster, Unit target, RestoreManaEffect effect)
    {
        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }
}
