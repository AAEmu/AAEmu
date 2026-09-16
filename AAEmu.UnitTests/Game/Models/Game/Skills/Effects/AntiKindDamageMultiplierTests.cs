using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The anti-NPC / anti-PC output multipliers (<c>unit_modifiers</c> 196-198 and 244-246) through the real
/// <see cref="DamageEffect"/> path, with a real caster and real victims rather than the rules class alone.
/// </summary>
/// <remarks>
/// The caster is a plain <see cref="Unit"/> carrying a buff whose row is the one under test, and its level
/// damage is the only damage source. <c>LevelDps * LevelMd</c> is 99.5 and the level formula adds the 0.5,
/// so the composed min and max are both exactly <see cref="ComposedDamage"/> before any multiplier: an
/// exact integer is what makes the hit deterministic, because the effect's own Floor/Ceiling widen a
/// non-integral composition into a range and the multiplier under test would then scale that range too.
/// The two victims are subclasses pinned to armour 0 so the test does not need the formula tables;
/// everything else about them (the kind test, buffs, aggro, packets) is the production class.
/// </remarks>
[NotInParallel]
public class AntiKindDamageMultiplierTests
{
    private const int ComposedDamage = 100;
    private const float LevelDpsForExactDamage = 199f; // 199 * 0.5f = 99.5f, + 0.5f = 100f
    private const float LevelMdForExactDamage = 0.5f;
    private const int TargetStartHp = 20_000;

    private EmptySkillManagerScope _skillManagerScope;

    [Before(Test)]
    public void InstallEmptySkillManager() => _skillManagerScope = new EmptySkillManagerScope();

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerScope?.Dispose();

    [Test]
    public async Task MeleeAntiNpc_ScalesTheHitOnAnNpc_AndLeavesAPlayerAlone()
    {
        var caster = CreateCaster((UnitAttribute.MeleeDamageMulAntiNpc, 1000L));

        var npc = CreateNpc();
        Hit(caster, npc, DamageType.Melee);
        await Assert.That(TargetStartHp - npc.Hp).IsEqualTo(ComposedDamage * 2);

        var player = CreatePlayer();
        Hit(caster, player, DamageType.Melee);
        await Assert.That(TargetStartHp - player.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task MeleeAntiPc_ScalesTheHitOnAPlayer_AndLeavesAnNpcAlone()
    {
        var caster = CreateCaster((UnitAttribute.MeleeDamageMulAntiPc, 1000L));

        var player = CreatePlayer();
        Hit(caster, player, DamageType.Melee);
        await Assert.That(TargetStartHp - player.Hp).IsEqualTo(ComposedDamage * 2);

        var npc = CreateNpc();
        Hit(caster, npc, DamageType.Melee);
        await Assert.That(TargetStartHp - npc.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task RangedVariants_UseTheirOwnAntiNpcAndAntiPcRows()
    {
        var antiNpcCaster = CreateCaster((UnitAttribute.RangedDamageMulAntiNpc, 1000L));
        var npc = CreateNpc();
        Hit(antiNpcCaster, npc, DamageType.Ranged);
        await Assert.That(TargetStartHp - npc.Hp).IsEqualTo(ComposedDamage * 2);

        var antiPcCaster = CreateCaster((UnitAttribute.RangedDamageMulAntiPc, 1000L));
        var player = CreatePlayer();
        Hit(antiPcCaster, player, DamageType.Ranged);
        await Assert.That(TargetStartHp - player.Hp).IsEqualTo(ComposedDamage * 2);
    }

    [Test]
    public async Task SpellVariants_UseTheirOwnAntiNpcAndAntiPcRows()
    {
        // 2500 is the shipped 폭주/광폭화 style row on the heal and spell ids; 2000 is the strongest
        // anti-PC row in the DB (buff 27590, on the melee id).
        var antiNpcCaster = CreateCaster((UnitAttribute.SpellDamageMulAntiNpc, 2500L));
        var npc = CreateNpc();
        Hit(antiNpcCaster, npc, DamageType.Magic);
        await Assert.That(TargetStartHp - npc.Hp).IsEqualTo((int)(ComposedDamage * 3.5f));

        var antiPcCaster = CreateCaster((UnitAttribute.SpellDamageMulAntiPc, 2000L));
        var player = CreatePlayer();
        Hit(antiPcCaster, player, DamageType.Magic);
        await Assert.That(TargetStartHp - player.Hp).IsEqualTo(ComposedDamage * 3);
    }

    [Test]
    public async Task WithoutSuchARow_EveryDamageTypeAndVictimIsUnchanged()
    {
        // The byte-identical pin: the composed hit is 100 with none of these attributes present, for each
        // damage type against both victim kinds.
        foreach (var damageType in new[] { DamageType.Melee, DamageType.Ranged, DamageType.Magic })
        {
            var caster = CreateCaster();

            var npc = CreateNpc();
            Hit(caster, npc, damageType);
            await Assert.That(TargetStartHp - npc.Hp).IsEqualTo(ComposedDamage);

            var player = CreatePlayer();
            Hit(caster, player, damageType);
            await Assert.That(TargetStartHp - player.Hp).IsEqualTo(ComposedDamage);
        }
    }

    [Test]
    public async Task AnAntiNpcRow_LeavesTheOtherDamageTypesAlone()
    {
        var caster = CreateCaster((UnitAttribute.MeleeDamageMulAntiNpc, 1000L));

        var rangedVictim = CreateNpc();
        Hit(caster, rangedVictim, DamageType.Ranged);
        await Assert.That(TargetStartHp - rangedVictim.Hp).IsEqualTo(ComposedDamage);

        var spellVictim = CreateNpc();
        Hit(caster, spellVictim, DamageType.Magic);
        await Assert.That(TargetStartHp - spellVictim.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task AVictimsOwnRow_IsNotUsed()
    {
        // The attribute is the attacker's output; a buff on the victim must not move it.
        var caster = CreateCaster();
        var npc = CreateNpc();
        TestBuffModifier.Apply(npc, UnitAttribute.MeleeDamageMulAntiNpc, 1000);

        Hit(caster, npc, DamageType.Melee);

        await Assert.That(TargetStartHp - npc.Hp).IsEqualTo(ComposedDamage);
    }

    [Test]
    public async Task AnIntimidationRowOfMinus1500_ZeroesTheHit_InsteadOfHealingTheVictim()
    {
        // buff 2102/2104/25747/25907 (위압감) carry -1500 on each anti-NPC id. Composed that is -0.5, and a
        // negative factor would have healed the NPC by 50.
        var caster = CreateCaster((UnitAttribute.MeleeDamageMulAntiNpc, -1500L));
        var npc = CreateNpc();

        Hit(caster, npc, DamageType.Melee);

        await Assert.That(npc.Hp).IsEqualTo(TargetStartHp);
    }

    private static Unit CreateCaster(params (UnitAttribute Attribute, long Value)[] rows)
    {
        var caster = new Unit
        {
            ObjId = 100,
            Level = 50,
            Hp = TargetStartHp,
            MaxHp = TargetStartHp,
            LevelDps = LevelDpsForExactDamage
        };

        for (var i = 0; i < rows.Length; i++)
            TestBuffModifier.Apply(caster, rows[i].Attribute, rows[i].Value, buffIndex: (uint)(i + 1));

        return caster;
    }

    private static void Hit(Unit caster, Unit victim, DamageType damageType)
    {
        var effect = new DamageEffect
        {
            Id = 1,
            DamageType = damageType,
            Multiplier = 1f,
            DpsIncMultiplier = 1f,
            // Level damage with no level variance and no weapon slot: the composition is exactly the
            // 100 of CreateCaster, so the roll cannot pick a different number.
            UseLevelDamage = true,
            LevelMd = LevelMdForExactDamage,
            WeaponSlotId = -1
        };

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

    // The template is only there so the aggro path can read EngageCombatGiveQuestId; the caster is not a
    // Character, so nothing else in it is consulted.
    private static TestNpc CreateNpc() =>
        new() { ObjId = 200, Level = 50, Hp = TargetStartHp, Template = new NpcTemplate() };

    private static TestPlayer CreatePlayer() => new() { ObjId = 201, Level = 50, Hp = TargetStartHp };

    /// <summary>An NPC with the formula-backed armour handlers pinned, so no formula data is needed.</summary>
    private sealed class TestNpc : Npc
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    /// <summary>The same for a player victim.</summary>
    private sealed class TestPlayer : CharacterMock
    {
        public override int Armor => 0;
        public override int MagicResistance => 0;
    }
}
