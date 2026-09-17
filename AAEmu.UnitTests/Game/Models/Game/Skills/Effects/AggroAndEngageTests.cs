using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <c>aggro_multiplier</c> and <c>engage_combat</c> through the real <see cref="DamageEffect"/> path, against
/// a real <see cref="Npc"/> victim so that <c>Npc.OnDamageReceived</c> runs and the aggro table is the one the
/// server keeps.
/// </summary>
[NotInParallel]
public class AggroAndEngageTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f;
    private const float LevelMdForExactDamage = 0.5f;
    private const int StartHp = 20_000;

    private EmptySkillManagerScope _skillManagerScope;

    [Before(Test)]
    public void InstallEmptySkillManager() => _skillManagerScope = new EmptySkillManagerScope();

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerScope?.Dispose();

    [Test]
    public async Task TheDefaultMultiplier_PutsTheDamageOnTheAggroTable()
    {
        // The 10,837 rows at 1.0: the threat is the damage, exactly as before.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect());

        await Assert.That(npc.AggroTable[caster.ObjId].DamageAggro).IsEqualTo(ComposedDamage);
        await Assert.That(StartHp - npc.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task ATenTimesMultiplier_PutsTenTimesTheDamageOnTheAggroTable()
    {
        // 방패 휘두르기 (damage effects 11583/12248): 10.0 on a 100 damage hit is 1,000 threat, and the
        // damage the victim takes is unchanged.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { AggroMultiplier = 10f });

        await Assert.That(npc.AggroTable[caster.ObjId].DamageAggro).IsEqualTo(ComposedDamage * 10);
        await Assert.That(StartHp - npc.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task AZeroMultiplier_PutsNothingOnTheAggroTable()
    {
        // The 77 rows that must pull nothing: 핏물먹이의 돌개바람, 극한의 얼음, 오스트 마력탑의 소환물 흡수,
        // 빛나는 해안 기지 자동 대포 발사.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { AggroMultiplier = 0f });

        await Assert.That(npc.AggroTable.TryGetValue(caster.ObjId, out var aggro) ? aggro.DamageAggro : 0)
            .IsEqualTo(0);
        await Assert.That(StartHp - npc.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task AScaledHit_StacksWithTheEarlierOnes()
    {
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { AggroMultiplier = 3f });
        Hit(caster, npc, new DamageEffect { AggroMultiplier = 3f });

        await Assert.That(npc.AggroTable[caster.ObjId].DamageAggro).IsEqualTo(ComposedDamage * 6);
    }

    [Test]
    public async Task EngageCombatOn_PutsBothUnitsInCombat()
    {
        // The default, and the 10,572 rows that leave it set.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { EngageCombat = true });

        await Assert.That(npc.IsInBattle).IsTrue();
        await Assert.That(caster.IsInBattle).IsTrue();
    }

    [Test]
    public async Task EngageCombatOff_LeavesBothUnitsOutOfCombat()
    {
        // The 429 rows that clear it — 자폭 비행, 감아올리기, 메테오 소환, 켈루스의 불덩이: the damage lands,
        // but the hit is not a fight.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { EngageCombat = false });

        await Assert.That(StartHp - npc.Hp).IsEqualTo(ComposedDamage);
        await Assert.That(npc.IsInBattle).IsFalse();
        await Assert.That(caster.IsInBattle).IsFalse();
    }

    [Test]
    public async Task EngageCombatOff_StillRecordsTheAggro()
    {
        // The two columns are independent: 'f' on engage_combat does not clear a non-zero aggro_multiplier,
        // and 35 of the 429 rows ship both.
        var caster = CreateCaster();
        var npc = CreateNpc();

        Hit(caster, npc, new DamageEffect { EngageCombat = false, AggroMultiplier = 1f });

        await Assert.That(npc.AggroTable[caster.ObjId].DamageAggro).IsEqualTo(ComposedDamage);
    }

    private static TestUnit CreateCaster() => new()
    {
        ObjId = 100,
        Level = 50,
        Hp = StartHp,
        MaxHp = StartHp,
        LevelDps = LevelDpsForExactDamage
    };

    private static TestNpc CreateNpc() => new() { ObjId = 200, Level = 50, Hp = StartHp, Template = new NpcTemplate() };

    private static void Hit(Unit caster, Unit victim, DamageEffect effect)
    {
        effect.Id = 1;
        effect.DamageType = DamageType.Melee;
        effect.Multiplier = 1f;
        effect.DpsIncMultiplier = 1f;
        effect.UseLevelDamage = true;
        effect.LevelMd = LevelMdForExactDamage;
        effect.WeaponSlotId = -1;

        effect.Apply(
            caster,
            new SkillCasterUnit(caster.ObjId),
            victim,
            new SkillCastUnitTarget(victim.ObjId),
            new CastSkill(1, 1),
            new EffectSource(),
            null,
            DateTime.UtcNow);
    }

    /// <summary>A caster with no armour and no world to broadcast into.</summary>
    private sealed class TestUnit : Unit
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    /// <summary>An NPC with the formula-backed handlers pinned, so no formula data is needed.</summary>
    private sealed class TestNpc : Npc
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    /// <summary>
    /// An NPC victim runs the aggro path (Npc.OnDamageReceived), and Unit.AddUnitAggro asks
    /// <c>SkillManager</c> for the buffs of the NoFight/Returning tags. An empty tag table answers "no such
    /// tag", which is what these tests want, and the previous singleton is put back afterwards.
    /// </summary>
    private sealed class EmptySkillManagerScope : IDisposable
    {
        private static readonly FieldInfo InstanceField =
            typeof(Singleton<SkillManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly object _previous;

        public EmptySkillManagerScope()
        {
            var skillManager = new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object);
            SetField(skillManager, "_buffTags", new Dictionary<uint, List<uint>>());
            _previous = InstanceField.GetValue(null);
            InstanceField.SetValue(null, skillManager);
        }

        public void Dispose() => InstanceField.SetValue(null, _previous);

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

            throw new InvalidOperationException($"Missing field {name}");
        }
    }
}
