using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>heal_effects.ignore_heal_aggro</c> (28 of 954 rows) through the real heal-to-aggro path: the heal
/// still lands, it just does not move the healed unit's attackers.
/// </summary>
/// <remarks>
/// The heal credit itself is <see cref="Unit.OnAbuserHealed"/>, the handler the healed unit hangs on its
/// attackers. It is driven here through the real <c>Events.OnHealed</c> raise rather than through
/// <see cref="Unit.AddUnitAggro"/>, because that call also broadcasts an aggro packet and needs a world
/// the heal path does not otherwise touch.
/// </remarks>
[NotInParallel]
public class HealEffectAggroTests
{
    private const int HealDpsRating = 100_000; // rating * 0.001f * DpsMultiplier = 100
    private const uint HealerObjId = 300;
    private const uint TargetObjId = 301;
    private const uint AttackerObjId = 302;

    private SingletonScope<SkillManager> _skills;

    /// <summary>The aggro credit reads buff tags, which resolve through <c>SkillManager.Instance</c>.</summary>
    [Before(Test)]
    public void InstallContentLookups() =>
        _skills = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());

    [After(Test)]
    public void RestoreContentLookups() => _skills.Dispose();

    [Test]
    public async Task TheHealReportsWhetherItMayRaiseAggro()
    {
        // The flag the effect authors is the flag the subscriber reads.
        var (healer, target, _) = CreateFight();

        var seen = default(OnHealedArgs);
        target.Events.OnHealed += (_, args) => seen = args;

        Heal(healer, target, ignoreHealAggro: true);

        await Assert.That(seen).IsNotNull();
        await Assert.That(seen.IgnoreHealAggro).IsTrue();
        await Assert.That(seen.HealAmount).IsEqualTo(100);
    }

    [Test]
    public async Task AnOrdinaryHealReportsTheOpposite()
    {
        var (healer, target, _) = CreateFight();

        var seen = default(OnHealedArgs);
        target.Events.OnHealed += (_, args) => seen = args;

        Heal(healer, target, ignoreHealAggro: false);

        await Assert.That(seen).IsNotNull();
        await Assert.That(seen.IgnoreHealAggro).IsFalse();
    }

    [Test]
    public async Task IgnoreHealAggro_StillPaysOutTheHeal()
    {
        var (healer, target, _) = CreateFight();

        var healed = Heal(healer, target, ignoreHealAggro: true);

        await Assert.That(healed).IsEqualTo(100);
    }

    [Test]
    public async Task TheSubscriber_CreditsAHealThatAllowsIt()
    {
        var (healer, _, attacker) = CreateFight();

        attacker.OnAbuserHealed(healer, new OnHealedArgs { Healer = healer, HealAmount = 100 });

        // Aggro.AddAggro keeps 60 % of a heal as the heal component.
        await Assert.That(HealAggroOf(attacker, healer)).IsEqualTo(60);
    }

    [Test]
    public async Task TheSubscriber_DropsAHealThatDoesNot()
    {
        var (healer, _, attacker) = CreateFight();

        attacker.OnAbuserHealed(healer, new OnHealedArgs
        {
            Healer = healer,
            HealAmount = 100,
            IgnoreHealAggro = true
        });

        await Assert.That(attacker.AggroTable.ContainsKey(healer.ObjId)).IsFalse();
    }

    [Test]
    public async Task IgnoreHealAggro_DoesNotSuppressTheOtherSubscribers()
    {
        // The flag travels on the event args, so anything else hanging off OnHealed still runs.
        var (healer, target, _) = CreateFight();
        var seen = 0;
        target.Events.OnHealed += (_, _) => seen++;

        Heal(healer, target, ignoreHealAggro: true);

        await Assert.That(seen).IsEqualTo(1);
    }

    private static (Unit Healer, Unit Target, Npc Attacker) CreateFight()
    {
        var healer = new Unit { ObjId = HealerObjId, Level = 50, Hp = 1, MaxHp = 100_000, HDps = HealDpsRating };
        var target = new Unit { ObjId = TargetObjId, Level = 50, Hp = 1, MaxHp = 100_000 };

        // The healed unit's attacker: the npc is the unit that carries the heal-aggro credit. It is built
        // with its aggro table already holding the entry and a template, because the call that would add
        // one reads npc.Template.EngageCombatGiveQuestId and broadcasts, and both need a world.
        var attacker = new Npc { ObjId = AttackerObjId, Template = new NpcTemplate() };
        attacker.AggroTable[target.ObjId] = new Aggro(target, new AggroComponents(500, 0, 0));

        return (healer, target, attacker);
    }

    private static int HealAggroOf(Npc npc, Unit unit) =>
        npc.AggroTable.TryGetValue(unit.ObjId, out var aggro) ? aggro.HealAggro : 0;

    private static int Heal(Unit healer, Unit target, bool ignoreHealAggro)
    {
        var startHp = target.Hp;

        new HealEffect
        {
            Id = 1,
            DpsMultiplier = 1f,
            IgnoreHealAggro = ignoreHealAggro
        }.Apply(
            healer,
            new SkillCasterUnit(healer.ObjId),
            target,
            new SkillCastUnitTarget(target.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);

        return target.Hp - startHp;
    }
}
