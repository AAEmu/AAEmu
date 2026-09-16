using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// <c>Unit.CalculateWithBonuses</c> and the hand-walked stat getters against a loaded
/// <c>unit_attribute_limits</c> table.
/// </summary>
// The limits live in a singleton, so a run of these tests must not overlap with the game-data tests
// that load and clear the same table.
[NotInParallel]
public class UnitAttributeClampTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE unit_attribute_limits (
                id INTEGER PRIMARY KEY,
                unit_attribute_id INTEGER NOT NULL,
                minimum integer(8) NOT NULL DEFAULT 0,
                maximum integer(8) NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Loads the given rows into the game data for the length of one test.</summary>
    private void LoadLimits(params (int Id, int AttributeId, long Minimum, long Maximum)[] rows)
    {
        using var clear = Connection.CreateCommand();
        clear.CommandText = "DELETE FROM unit_attribute_limits";
        clear.ExecuteNonQuery();

        foreach (var (id, attributeId, minimum, maximum) in rows)
        {
            using var insert = Connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO unit_attribute_limits (id, unit_attribute_id, minimum, maximum) " +
                "VALUES (@id, @attributeId, @minimum, @maximum)";
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@attributeId", attributeId);
            insert.Parameters.AddWithValue("@minimum", minimum);
            insert.Parameters.AddWithValue("@maximum", maximum);
            insert.ExecuteNonQuery();
        }

        UnitAttributeLimitGameData.Instance.Load(Connection);
    }

    private static void AddFlat(Unit unit, UnitAttribute attribute, long value) =>
        unit.AddBonus(1u, new Bonus
        {
            Template = new BonusTemplate { Attribute = attribute, ModifierType = UnitModifierType.Value },
            Value = value
        });

    private static void AddPercent(Unit unit, UnitAttribute attribute, long value) =>
        unit.AddBonus(1u, new Bonus
        {
            Template = new BonusTemplate { Attribute = attribute, ModifierType = UnitModifierType.Percent },
            Value = value
        });

    [Test]
    public async Task AttributeWithALimitRow_ClampsTheComposedValue()
    {
        var unit = new Unit();
        // Two flats (the second is what pushes past the row) and then a percent on the total: the clamp
        // runs on the composed value, not on any one bonus.
        AddFlat(unit, UnitAttribute.MoveSpeedMul, 20000);
        AddFlat(unit, UnitAttribute.MoveSpeedMul, 5000);

        try
        {
            // unit_attribute_limits (2, 10, -10000, 8000) — move_speed_mul is composed in per-mille.
            LoadLimits((2, 10, -10000, 8000));

            var flat = unit.CalculateWithBonuses(1000, UnitAttribute.MoveSpeedMul);
            await Assert.That(flat).IsEqualTo(8000d);

            var percent = new Unit();
            AddPercent(percent, UnitAttribute.MoveSpeedMul, 800);
            // 1000 + 800% = 9000, capped at 8000.
            await Assert.That(percent.CalculateWithBonuses(1000, UnitAttribute.MoveSpeedMul)).IsEqualTo(8000d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task AttributeWithALimitRow_ClampsUpToTheMinimumToo()
    {
        var unit = new Unit();
        AddFlat(unit, UnitAttribute.GlobalCooldownMul, -1000);

        try
        {
            // unit_attribute_limits (1, 74, -666, 2000) — global_cooldown_mul.
            LoadLimits((1, 74, -666, 2000));

            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.GlobalCooldownMul)).IsEqualTo(-666d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task AttributeWithoutALimitRow_IsUntouched()
    {
        var unit = new Unit();
        AddFlat(unit, UnitAttribute.Mass, 26000);
        AddFlat(unit, UnitAttribute.LungCapacity, 900000);

        try
        {
            LoadLimits((2, 10, -10000, 8000));

            // Mass (188) and LungCapacity (91) have no row, so their large values survive.
            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.Mass)).IsEqualTo(26000d);
            await Assert.That(unit.CalculateWithBonuses(60000, UnitAttribute.LungCapacity)).IsEqualTo(960000d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task WithNoTableLoaded_NothingIsClamped()
    {
        var unit = new Unit();
        AddFlat(unit, UnitAttribute.MoveSpeedMul, 20000);

        UnitAttributeLimitGameData.Instance.ClearForTests();

        await Assert.That(unit.CalculateWithBonuses(1000, UnitAttribute.MoveSpeedMul)).IsEqualTo(21000d);
    }

    [Test]
    public async Task DropRateMul_RowIsInTheClientScale_SoTheDeltaIsNotClamped()
    {
        var unit = new Unit();
        AddPercent(unit, UnitAttribute.DropRateMul, 50);

        try
        {
            // unit_attribute_limits (27, 140, 100, 2000000000): the table stores the absolute rate
            // (100 = 1x) while Character.DropRateMul composes the delta and LootPack adds the 100 itself
            // as `(100 + DropRateMul) / 100`. Clamping the delta up to 100 would double every loot roll,
            // so the row is skipped for this attribute whichever base the caller passes - the 125 shipped
            // flat rows below the row's minimum stay live. See UnitAttributeLimitRules.
            LoadLimits((27, 140, 100, 2000000000));

            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.DropRateMul)).IsEqualTo(0d);
            await Assert.That(unit.CalculateWithBonuses(100, UnitAttribute.DropRateMul)).IsEqualTo(150d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task ExpMul_RowIsInTheClientScale_SoTheShippedPenaltiesSurvive()
    {
        var penalty = new Unit();
        AddFlat(penalty, UnitAttribute.ExpMul, -50);
        var harsh = new Unit();
        AddFlat(harsh, UnitAttribute.ExpMul, -500);

        try
        {
            // unit_attribute_limits (25, 95, 0, 500): exp_mul's row is the client's absolute scale while
            // Character.ExpMul composes the delta AddExp adds 100 to. Unlike drop_rate_mul, 0 is inside
            // this row, so applying it would floor the shipped -50 (buff 27888) and -500 (npc templates
            // 13444, 16553, 16554) at 0 and make those penalties inert. The row is skipped instead.
            LoadLimits((25, 95, 0, 500));

            await Assert.That(penalty.CalculateWithBonuses(0, UnitAttribute.ExpMul)).IsEqualTo(-50d);
            await Assert.That(harsh.CalculateWithBonuses(0, UnitAttribute.ExpMul)).IsEqualTo(-500d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task LivingPointGainMul_RowIsInTheClientScale_SoTheDeltaIsNotClamped()
    {
        var unit = new Unit();
        AddFlat(unit, UnitAttribute.LivingPointGainMul, -150);

        try
        {
            // unit_attribute_limits (44, 137, -100, 2000000000): the third row of that shape, skipped for
            // the same reason - the award site adds the 100 baseline itself, so a -150 delta is not the
            // row's -100 absolute minimum.
            LoadLimits((44, 137, -100, 2000000000));

            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.LivingPointGainMul)).IsEqualTo(-150d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task LivingPointGain_IsComposedInTheRowsScale_SoTheRowApplies()
    {
        var unit = new Unit();
        AddFlat(unit, UnitAttribute.LivingPointGain, 200);

        try
        {
            // unit_attribute_limits (43, 136, -2000000000, 50): the flat sibling is not a delta, so the
            // row's 50 caps the shipped +200 (buff 28444) and +100 (item 50762) rows.
            LoadLimits((43, 136, -2000000000, 50));

            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.LivingPointGain)).IsEqualTo(50d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task SpellCriticalBonus_TwoBoundedAttributesEachKeepTheirOwnRow()
    {
        var character = new CharacterMock();
        AddFlat(character, UnitAttribute.SpellCriticalBonus, 6000); // past row 31's 4500
        AddFlat(character, UnitAttribute.SpellDamageCriticalBonus, 100); // well inside row 152's 4500

        try
        {
            // (24, 31, -2000000000, 4500) spell_critical_bonus
            // (45, 152, -2000000000, 4500) spell_damage_critical_bonus
            LoadLimits((24, 31, -2000000000, 4500), (45, 152, -2000000000, 4500));

            // 1500 baseline + 6000 -> row 31's 4500, and 0 + 100 -> 100 on row 152's own scale, so the
            // property is (4500 + 100 - 1000) / 10 = 360. Running one accumulator through both rows would
            // report 350: row 31's bound would have swallowed attribute 152's 100.
            await Assert.That(character.SpellCriticalBonus).IsEqualTo(360f);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task SpellCriticalMul_TwoBoundedAttributesEachKeepTheirOwnRow()
    {
        var character = new CharacterMock();
        AddFlat(character, UnitAttribute.SpellCriticalMul, 5000); // past row 86's 1000
        AddFlat(character, UnitAttribute.SpellDamageCriticalMul, 200); // inside row 151's 1000

        try
        {
            // (40, 86, -2000000000, 1000) spell_critical_mul
            // (46, 151, -2000000000, 1000) spell_damage_critical_mul
            LoadLimits((40, 86, -2000000000, 1000), (46, 151, -2000000000, 1000));

            // 5000 -> 1000 on row 86 and 200 stays on row 151, so the property is 1200. One accumulator
            // through both rows would report 1000.
            await Assert.That(character.SpellCriticalMul).IsEqualTo(1200f);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task NpcArmor_HandWalkedStat_ClampsToTheArmourRow()
    {
        using var formulas = ConstantFormulas(FormulaOwnerType.Npc, 1000d);
        var npc = new Npc { Template = new NpcTemplate { Id = 1 } };
        // unit_modifiers ships seven owner_type 'Slave' rows of exactly +5000000 armour and magic resist.
        AddFlat(npc, UnitAttribute.Armor, 5000000);

        UnitAttributeLimitGameData.Instance.ClearForTests();
        await Assert.That(npc.Armor).IsEqualTo(5001000);

        try
        {
            // unit_attribute_limits (5, 8, 0, 2000000): Npc.Armor walks GetBonuses itself, so it has to
            // clamp its own result - it never reaches CalculateWithBonuses.
            LoadLimits((5, 8, 0, 2000000));

            await Assert.That(npc.Armor).IsEqualTo(2000000);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task NpcMagicResistance_HandWalkedStat_ClampsToTheMagicResistRow()
    {
        using var formulas = ConstantFormulas(FormulaOwnerType.Npc, 1000d);
        var npc = new Npc { Template = new NpcTemplate { Id = 1 } };
        AddFlat(npc, UnitAttribute.MagicResist, 5000000);

        try
        {
            // unit_attribute_limits (22, 64, 0, 2000000).
            LoadLimits((22, 64, 0, 2000000));

            await Assert.That(npc.MagicResistance).IsEqualTo(2000000);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task SlaveArmor_HandWalkedStat_ClampsToTheArmourRow()
    {
        using var formulas = ConstantFormulas(FormulaOwnerType.Slave, 1000d);
        var slave = new Slave();
        AddFlat(slave, UnitAttribute.Armor, 5000000);

        try
        {
            LoadLimits((5, 8, 0, 2000000));

            await Assert.That(slave.Armor).IsEqualTo(2000000);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task MateArmor_HandWalkedStat_ClampsToTheArmourRow()
    {
        using var formulas = ConstantFormulas(FormulaOwnerType.Mate, 1000d);
        var mate = new Mate();
        AddFlat(mate, UnitAttribute.Armor, 5000000);

        try
        {
            LoadLimits((5, 8, 0, 2000000));

            await Assert.That(mate.Armor).IsEqualTo(2000000);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    [Test]
    public async Task ShipyardMaxHp_HandWalkedStat_StaysInsideItsWiderRow()
    {
        using var formulas = ConstantFormulas(FormulaOwnerType.Shipyard, 1000d);
        var shipyard = new Shipyard();
        AddFlat(shipyard, UnitAttribute.MaxHealth, 5000);

        try
        {
            // unit_attribute_limits (1, 6, -2000000000, 1000000000000) is wider than an int, so an
            // int-returning hand-walked getter is inside it either way: the clamp is applied for
            // uniformity and must not disturb the value. The armour and magic-resist rows are the ones
            // that bite on these paths.
            LoadLimits((1, 6, -2000000000, 1000000000000));

            await Assert.That(shipyard.MaxHp).IsEqualTo(6000);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }

    /// <summary>
    /// A <see cref="FormulaManager"/> whose every unit formula for <paramref name="owner"/> evaluates to
    /// <paramref name="baseValue"/>, so a stat getter that walks its bonuses by hand can be exercised
    /// without the content database. Dispose it with <c>using</c> to restore the previous singleton.
    /// </summary>
    private static SingletonScope<FormulaManager> ConstantFormulas(FormulaOwnerType owner, double baseValue)
    {
        var formulas = new FormulaManager();
        SetField(formulas, "_unitVariables",
            new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>());

        var byKind = new Dictionary<UnitFormulaKind, UnitFormula>();
        var id = 1u;
        foreach (var kind in Enum.GetValues<UnitFormulaKind>())
        {
            var formula = new UnitFormula { Id = id++, Kind = kind, Owner = owner };
            SetField(formula, "<Expression>k__BackingField",
                (Func<Dictionary<string, double>, double>)(_ => baseValue));
            byKind[kind] = formula;
        }

        SetField(formulas, "_unitFormulas",
            new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>> { [owner] = byKind });

        return new SingletonScope<FormulaManager>(formulas);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"Missing field {name}");
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;
        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }
        public void Dispose() => _field.SetValue(null, _previous);
    }
}
