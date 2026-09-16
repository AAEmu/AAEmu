using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// The NPC-side combat ratings that read <c>unit_formulas</c> owner <c>npc</c> rows: critical chance, the
/// critical bonuses, dodge, parry, block, facets and the heal critical pair. Before this batch every one of
/// them stayed at the <see cref="Unit"/> defaults (0), so an NPC could not crit or avoid anything.
/// </summary>
// The formulas live in a singleton; a parallel test installing its own FormulaManager would swap it
// mid-assertion.
[NotInParallel]
public class NpcCombatStatTests
{
    private const byte TemplateId = 1;
    private const byte KindId = 1;   // enum_npc_kind 1 = human
    private const byte GradeId = 1;
    private const int Level = 50;

    /// <summary>
    /// The 10.0.2.13 rows this test needs, verbatim from <c>unit_formulas</c> (owner_type_id 1). The stat
    /// formulas are here because the ratings are evaluated on top of them: an NPC's strength is itself a row.
    /// </summary>
    private static readonly (UnitFormulaKind Kind, string Text)[] NpcRows =
    [
        (UnitFormulaKind.Str, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Dex, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Sta, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Int, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Spi, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Fai, "( (level + heir_level) * 4 + 10 ) * npc_template"),
        (UnitFormulaKind.Facet, "((level ^ 1.3 + level * 3 + 17) * 100 ) * 100"),
        (UnitFormulaKind.MeleeCritical, "( str * 4 * npc_kind ) * 100"),
        (UnitFormulaKind.RangedCritical, "((dex + int) / 2 * 4 ) * 100"),
        (UnitFormulaKind.SpellCritical, "(int * 4 * npc_kind ) * 100"),
        (UnitFormulaKind.MeleeCriticalBonus, "1500"),
        (UnitFormulaKind.RangedCriticalBonus, "1500"),
        (UnitFormulaKind.SpellCriticalBonus, "1500"),
        (UnitFormulaKind.Dodge, "((dex + int) / 2 * 1 ) * 100"),
        (UnitFormulaKind.MeleeParry, "((str + sta) / 2 * 0 ) * 100"),
        (UnitFormulaKind.RangedParry, "0"),
        (UnitFormulaKind.Block, "(str * 0 ) * 100"),
        (UnitFormulaKind.HealCritical, "(spi * 4 * npc_kind ) * 100"),
        (UnitFormulaKind.HealCriticalBonus, "1500")
    ];

    /// <summary>Installs the rows above with their npc_template/npc_kind/npc_grade variables at 1.</summary>
    private static SingletonScope<FormulaManager> WithNpcRows()
    {
        var manager = new FormulaManager();
        var byKind = new Dictionary<UnitFormulaKind, UnitFormula>();
        var variables = new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();

        var id = 1u;
        foreach (var (kind, text) in NpcRows)
        {
            var formula = new UnitFormula
            {
                Id = id++,
                Kind = kind,
                Owner = FormulaOwnerType.Npc,
                TextFormula = text
            };
            formula.Prepare();
            byKind[kind] = formula;

            variables[formula.Id] = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>
            {
                [UnitFormulaVariableType.NpcTemplate] = Keyed(formula.Id, UnitFormulaVariableType.NpcTemplate, TemplateId),
                [UnitFormulaVariableType.NpcKind] = Keyed(formula.Id, UnitFormulaVariableType.NpcKind, KindId),
                [UnitFormulaVariableType.NpcGrade] = Keyed(formula.Id, UnitFormulaVariableType.NpcGrade, GradeId)
            };
        }

        SetField(manager, "_unitFormulas",
            new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>
            {
                [FormulaOwnerType.Npc] = byKind
            });
        SetField(manager, "_unitVariables", variables);

        return new SingletonScope<FormulaManager>(manager);
    }

    private static Dictionary<uint, UnitFormulaVariable> Keyed(uint formulaId, UnitFormulaVariableType type, uint key) =>
        new()
        {
            [key] = new UnitFormulaVariable { FormulaId = formulaId, Type = type, Key = key, Value = 1f }
        };

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

    private static Npc Spawn() => new TestNpc
    {
        ObjId = 200,
        Level = Level,
        Hp = 1000,
        MaxHp = 1000,
        Template = new NpcTemplate
        {
            NpcTemplateId = (NpcTemplateType)TemplateId,
            NpcKindId = (NpcKindType)KindId,
            NpcGradeId = (NpcGradeType)GradeId
        }
    };

    /// <summary>An NPC whose broadcast path is a no-op, so a roll can be taken in isolation.</summary>
    private sealed class TestNpc : Npc
    {
        public override void BroadcastPacket(AAEmu.Game.Core.Network.Game.GamePacket packet, bool self) { }
    }

    [Test]
    public async Task SpawnedNpc_CriticalAndDodgeComeFromTheFormulaRows()
    {
        using var scope = WithNpcRows();
        var npc = Spawn();

        // The rows themselves: strength is ((level + heir_level) * 4 + 10) * npc_template = 210 at level 50,
        // and facets are ((level ^ 1.3 + level * 3 + 17) * 100) * 100 = 3,286,8xx. Every rating is the row
        // over facets in per-cent, the same normalisation the player-side getters use.
        var facets = ((Math.Pow(Level, 1.3) + Level * 3 + 17) * 100) * 100;
        var str = (Level * 4 + 10) * 1d;

        await Assert.That(npc.Str).IsEqualTo(210);
        await Assert.That(npc.Facets).IsEqualTo((int)facets);
        await Assert.That(npc.MeleeCritical).IsGreaterThan(0f);
        await Assert.That(npc.DodgeRate).IsGreaterThan(0f);
        await Assert.That((double)npc.MeleeCritical).IsEqualTo(str * 4 * KindId * 100 / facets * 100).Within(1e-3);
        await Assert.That((double)npc.DodgeRate).IsEqualTo(str * 100 / facets * 100).Within(1e-3);
        await Assert.That((double)npc.SpellCritical).IsEqualTo(str * 4 * KindId * 100 / facets * 100).Within(1e-3);
        await Assert.That((double)npc.RangedCritical).IsEqualTo(str * 4 * 100 / facets * 100).Within(1e-3);
        await Assert.That((double)npc.HealCritical).IsEqualTo(str * 4 * KindId * 100 / facets * 100).Within(1e-3);

        // The bonuses are per-mille above 1000, exactly as the character getters read them: 1500 -> 50.
        await Assert.That(npc.MeleeCriticalBonus).IsEqualTo(50f);
        await Assert.That(npc.RangedCriticalBonus).IsEqualTo(50f);
        await Assert.That(npc.SpellCriticalBonus).IsEqualTo(50f);
        await Assert.That(npc.HealCriticalBonus).IsEqualTo(50f);

        // The NPC rows for these two really are 0, so 0 is the formula's answer rather than a missing reader.
        await Assert.That(npc.MeleeParryRate).IsEqualTo(0f);
        await Assert.That(npc.RangedParryRate).IsEqualTo(0f);
        await Assert.That(npc.BlockRate).IsEqualTo(0f);
    }

    [Test]
    public async Task NpcWithoutTheRows_KeepsTheZeroRatingsItAlwaysHad()
    {
        // The absence pin: a content root whose unit_formulas has no npc owner leaves the ratings exactly at
        // the Unit defaults, so nothing about a hit changes.
        using var scope = new SingletonScope<FormulaManager>(new FormulaManager());
        var npc = Spawn();

        await Assert.That(npc.Facets).IsEqualTo(0);
        await Assert.That(npc.MeleeCritical).IsEqualTo(0f);
        await Assert.That(npc.RangedCritical).IsEqualTo(0f);
        await Assert.That(npc.SpellCritical).IsEqualTo(0f);
        await Assert.That(npc.DodgeRate).IsEqualTo(0f);
        await Assert.That(npc.MeleeParryRate).IsEqualTo(0f);
        await Assert.That(npc.BlockRate).IsEqualTo(0f);
        await Assert.That(npc.MeleeCriticalBonus).IsEqualTo(0f);
        await Assert.That(npc.HealCritical).IsEqualTo(0f);
    }

    [Test]
    public async Task NpcBonusRows_ComposeOntoTheFormulaRating()
    {
        using var scope = WithNpcRows();
        var npc = Spawn();
        var withoutBonus = npc.DodgeRate;

        // unit_modifiers owner_type='Npc' attribute 178 (dodge) rows reach the rating through the same
        // CalculateWithBonuses walk the hand-written getters use.
        npc.AddBonus(1u, new Bonus
        {
            Template = new BonusTemplate
            {
                Attribute = UnitAttribute.Dodge,
                ModifierType = UnitModifierType.Value,
                Value = 1000
            },
            Value = 1000
        });

        await Assert.That((double)npc.DodgeRate).IsEqualTo((double)withoutBonus + 1000d / npc.Facets * 100d).Within(1e-3);
    }

    [Test]
    public async Task RollCombatDice_CanDodgeAndHitAgainstAnNpc()
    {
        using var scope = WithNpcRows();
        var npc = Spawn();
        var attacker = new Unit { ObjId = 100, Level = Level };
        var skill = new Skill { Template = new SkillTemplate { Id = 1, DamageTypeId = (uint)DamageType.Melee }, Level = 1 };

        await Assert.That(npc.DodgeRate).IsGreaterThan(0f);

        // 0.64 per-cent a roll at level 50, so over this many rolls a dodge is certain in practice while the
        // hits that come with it keep the result from being degenerate.
        var dodges = 0;
        var hits = 0;
        for (var i = 0; i < 4000; i++)
        {
            switch (skill.RollCombatDice(attacker, npc))
            {
                case SkillHitType.MeleeDodge:
                    dodges++;
                    break;
                case SkillHitType.MeleeHit:
                    hits++;
                    break;
            }
        }

        await Assert.That(dodges).IsGreaterThan(0);
        await Assert.That(hits).IsGreaterThan(0);
    }
}
