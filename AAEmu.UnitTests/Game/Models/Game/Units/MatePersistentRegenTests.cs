using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The mate's two in-combat regeneration getters, pinned to the <c>unit_formulas</c> rows that drive them.
/// </summary>
/// <remarks>
/// <para>
/// The two rows are one shape in two colours. Owner 5 (mate) kind 31 is <c>' ( sta * 0.1 ) * 2'</c> and
/// kind 32 is <c>' ( spi * 0.1 ) * 2'</c> — the same expression over a different attribute — so the only
/// thing that may differ between the health and mana getters is which stat the row reads. Nothing in the
/// data divides either result.
/// </para>
/// <para>
/// The mana getter divided the evaluated row by 5 while the health getter did not, so a mate in combat
/// regenerated a fifth of the mana its content asks for and one creature's two bars drained at unrelated
/// rates. These tests evaluate the shipped expressions verbatim and compare the getters against the row,
/// so a divide reintroduced on either side fails here.
/// </para>
/// </remarks>
// The rows live in the FormulaManager singleton; a parallel test swapping it would change the answer
// mid-assertion.
[NotInParallel]
public class MatePersistentRegenTests
{
    private const byte MateKindId = 15;

    /// <summary>
    /// The <c>unit_formulas</c> owner 5 rows this test needs. The persistent pair are the shipped
    /// expressions verbatim; the attribute rows are the ones the getters feed into them.
    /// </summary>
    private static readonly (UnitFormulaKind Kind, string Text)[] MateRows =
    [
        (UnitFormulaKind.Str, "0"),
        (UnitFormulaKind.Dex, "0"),
        (UnitFormulaKind.Sta, "( level * 4 ) + 10"),
        (UnitFormulaKind.Int, "( level * 4 ) + 10"),
        (UnitFormulaKind.Spi, "( level * 4 ) + 10"),
        (UnitFormulaKind.Fai, "( level * 4 ) + 10"),
        (UnitFormulaKind.PersistentHealthRegen, " ( sta * 0.1 ) * 2"),
        (UnitFormulaKind.PersistentManaRegen, " ( spi * 0.1 ) * 2")
    ];

    /// <summary>Installs <see cref="MateRows"/> with the mate_kind variable at 1.</summary>
    private static SingletonScope<FormulaManager> WithMateRows()
    {
        var manager = new FormulaManager();
        var byKind = new Dictionary<UnitFormulaKind, UnitFormula>();
        var variables = new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();

        var id = 1u;
        foreach (var (kind, text) in MateRows)
        {
            var formula = new UnitFormula
            {
                Id = id++,
                Kind = kind,
                Owner = FormulaOwnerType.Mate,
                TextFormula = text
            };
            formula.Prepare();
            byKind[kind] = formula;

            variables[formula.Id] = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>
            {
                [UnitFormulaVariableType.MateKind] = new Dictionary<uint, UnitFormulaVariable>
                {
                    [MateKindId] = new UnitFormulaVariable
                    {
                        FormulaId = formula.Id,
                        Type = UnitFormulaVariableType.MateKind,
                        Key = MateKindId,
                        Value = 1f
                    }
                }
            };
        }

        SetField(manager, "_unitFormulas",
            new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>
            {
                [FormulaOwnerType.Mate] = byKind
            });
        SetField(manager, "_unitVariables", variables);

        return new SingletonScope<FormulaManager>(manager);
    }

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

    private static Mate Spawn(int level) => new()
    {
        ObjId = 300,
        TlId = 300,
        Level = (byte)level,
        Hp = 1000,
        MaxHp = 1000,
        Mp = 1000,
        MaxMp = 1000,
        Template = new NpcTemplate { Id = 1, MateKindId = MateKindId, Level = (byte)level }
    };

    [Test]
    public async Task PersistentRegen_IsTheContentRowVerbatimOnBothSides()
    {
        using var scope = WithMateRows();
        var mate = Spawn(level: 50);

        // sta and spi are both ( 50 * 4 ) + 10 = 210, so the two persistent rows evaluate to the same
        // number here: ( 210 * 0.1 ) * 2 = 42. Neither getter may scale that.
        await Assert.That(mate.Sta).IsEqualTo(210);
        await Assert.That(mate.Spi).IsEqualTo(210);
        await Assert.That(mate.PersistentHpRegen).IsEqualTo(42);
        await Assert.That(mate.PersistentMpRegen).IsEqualTo(42);
    }

    [Test]
    public async Task PersistentManaRegen_TracksSpiWhileHealthTracksSta()
    {
        using var scope = WithMateRows();

        // Two mates whose levels make the two rows disagree, so a getter reading the wrong attribute, or
        // scaling the row, cannot land on the right answer by accident.
        var high = Spawn(level: 50);   // stat 210 -> ( 210 * 0.1 ) * 2 = 42
        var low = Spawn(level: 30);    // stat 130 -> ( 130 * 0.1 ) * 2 = 26

        await Assert.That(high.PersistentHpRegen).IsEqualTo(42);
        await Assert.That(low.PersistentHpRegen).IsEqualTo(26);
        await Assert.That(high.PersistentMpRegen).IsEqualTo(42);
        await Assert.That(low.PersistentMpRegen).IsEqualTo(26);

        // A fifth of the row, which is what the stray divide produced, is not any of these answers.
        await Assert.That(high.PersistentMpRegen).IsNotEqualTo(42 / 5);
        await Assert.That(low.PersistentMpRegen).IsNotEqualTo(26 / 5);
        await Assert.That(high.PersistentHpRegen).IsNotEqualTo(42 / 5);
        await Assert.That(low.PersistentHpRegen).IsNotEqualTo(26 / 5);
    }

    [Test]
    public async Task PersistentManaRegen_DoesNotTruncateToZeroOnASmallRow()
    {
        using var scope = WithMateRows();
        var mate = Spawn(level: 1);

        // ( 1 * 4 ) + 10 = 14, so the row is ( 14 * 0.1 ) * 2 = 2.8 -> 2, and 2 / 5 is 0 in integer
        // arithmetic: a mate that low in spirit regenerated no mana at all in combat while its health bar
        // still moved. Both getters must return the row, not a truncated share of it.
        await Assert.That(mate.PersistentMpRegen).IsEqualTo((int)(mate.Spi * 0.1 * 2));
        await Assert.That(mate.PersistentHpRegen).IsEqualTo((int)(mate.Sta * 0.1 * 2));
        await Assert.That(mate.PersistentMpRegen).IsGreaterThan(0);
        await Assert.That(mate.PersistentHpRegen).IsGreaterThan(0);
    }
}
