using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using TUnit.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// AoE diminishing through the real <see cref="DamageEffect"/> path: three hits of the same area skill on one
/// unit deal its full damage, 95 % and 90 %.
/// </summary>
/// <remarks>
/// Siege is the damage type with no armor reduction and no critical roll, so the composed hit is the number
/// the rate multiplies. The skill carries its own <c>target_area_radius</c>, which is what makes it an area
/// skill; the plot flag is not needed for this shape and none of these skills names a plot.
/// </remarks>
[NotInParallel]
public class AoeDiminishingDamageTests
{
    private const uint SkillId = 11939; // 불의 비, target_area_count 3 / target_area_radius 10
    private const int AreaRadius = 10;
    private const int VictimMaxHp = 1_000_000;

    private static readonly int[] Shipped = [100, 95, 90, 85, 80, 75, 70, 65, 60, 50];

    private static readonly FieldInfo SkillManagerField = typeof(Singleton<SkillManager>)
        .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private object _previousSkillManager;
    private readonly List<(uint Id, int Rate)> _ratesToRestore = [];

    [Before(Test)]
    public void InstallSkillManager()
    {
        _previousSkillManager = SkillManagerField.GetValue(null);
        SkillManagerField.SetValue(null,
            new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));

        AoeDiminishingTable.Clear();
        foreach (var rate in Shipped)
            AoeDiminishingTable.Add((uint)(_ratesToRestore.Count + 1), rate);
        AoeDiminishingTable.Seal();
    }

    [After(Test)]
    public void Restore()
    {
        AoeDiminishingTable.Clear();
        AoeDiminishingTable.Seal();
        SkillManagerField.SetValue(null, _previousSkillManager);
    }

    [Test]
    public async Task ThreeHitsOfTheSameAreaSkill_Deal100Then95Then90Percent()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim);
        var second = Hit(caster, victim);
        var third = Hit(caster, victim);

        // The composed hit is the same every time; only the rate moves it.
        await Assert.That(second).IsEqualTo((int)(first * 0.95f));
        await Assert.That(third).IsEqualTo((int)(first * 0.90f));
    }

    [Test]
    public async Task WithoutTheTable_TheHitIsExactlyWhatItWas()
    {
        // The neutrality pin: an unloaded table is a factor of exactly 1.0f, so three hits are identical.
        AoeDiminishingTable.Clear();
        AoeDiminishingTable.Seal();

        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim);
        var second = Hit(caster, victim);
        var third = Hit(caster, victim);

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(third).IsEqualTo(first);
    }

    [Test]
    public async Task ASingleTargetSkill_DoesNotDiminish()
    {
        // target_area_radius 0 and target_area_count 1: not an area skill, whatever the table says.
        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim, areaRadius: 0, areaCount: 1);
        var second = Hit(caster, victim, areaRadius: 0, areaCount: 1);

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task TwoDifferentAreaSkills_DoNotShareTheDecay()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim);
        Hit(caster, victim);
        var otherSkillFirst = Hit(caster, victim, skillId: SkillId + 1);

        await Assert.That(otherSkillFirst).IsEqualTo(first);
    }

    [Test]
    public async Task AnAfterTheWindowHit_IsBackToFull()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim);
        Hit(caster, victim);
        var afterWindow = Hit(caster, victim, at: DateTime.UtcNow.AddSeconds(AoeDiminishingRules.WindowSeconds + 1));

        await Assert.That(afterWindow).IsEqualTo(first);
    }

    [Test]
    public async Task TheResetEffect_PutsTheSequenceBackToFull()
    {
        var caster = CreateCaster();
        var victim = CreateVictim();
        AoeDiminishingTracker.Reset(victim.ObjId);

        var first = Hit(caster, victim);
        Hit(caster, victim);

        new ResetAoeDiminishingEffect { Id = 1 }.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            victim,
            new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);

        var afterReset = Hit(caster, victim);

        await Assert.That(afterReset).IsEqualTo(first);
    }

    private static Unit CreateCaster() =>
        new() { ObjId = 900, Level = 50, DpsInc = 0, IncomingDamageMul = 1f };

    private static Unit CreateVictim() =>
        new() { ObjId = 901, Level = 50, Hp = VictimMaxHp, MaxHp = VictimMaxHp, IncomingDamageMul = 1f };

    /// <summary>One hit and the health it took. A fresh caster per hit is not needed: the counter is the victim's.</summary>
    private static int Hit(Unit caster, Unit victim, uint skillId = SkillId, int areaRadius = AreaRadius,
        int areaCount = 3, DateTime? at = null)
    {
        var before = victim.Hp;

        var skill = new Skill
        {
            Template = new SkillTemplate
            {
                Id = skillId,
                CastingTime = 1000,
                TargetAreaRadius = areaRadius,
                TargetAreaCount = areaCount
            },
            Level = 1
        };

        var effect = new DamageEffect
        {
            Id = 11939,
            DamageType = DamageType.Siege, // no armor reduction, no critical roll
            Multiplier = 1f,
            UseLevelDamage = true,
            LevelMd = 1f,
            LevelVaStart = 100,
            LevelVaEnd = 100,
            WeaponSlotId = -1
        };

        effect.Apply(caster, new SkillCasterUnit(caster.ObjId), victim, new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(skillId, 1), new EffectSource(skill), null, at ?? DateTime.UtcNow);

        return before - victim.Hp;
    }
}
