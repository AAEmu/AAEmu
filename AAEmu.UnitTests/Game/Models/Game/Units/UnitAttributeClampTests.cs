using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// <c>Unit.CalculateWithBonuses</c> against a loaded <c>unit_attribute_limits</c> table.
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
    public async Task DropRateMul_KeepsTheServerScale()
    {
        var unit = new Unit();
        AddPercent(unit, UnitAttribute.DropRateMul, 50);

        try
        {
            // unit_attribute_limits (27, 140, 100, 2000000000): the table stores the absolute rate
            // (100 = 1x) while Character.DropRateMul composes the delta and LootPack adds the 100
            // itself as `(100 + DropRateMul) / 100`. Clamping the delta up to 100 would double every
            // loot roll, so a base outside the row leaves it alone.
            LoadLimits((27, 140, 100, 2000000000));

            await Assert.That(unit.CalculateWithBonuses(0, UnitAttribute.DropRateMul)).IsEqualTo(0d);
            await Assert.That(unit.CalculateWithBonuses(100, UnitAttribute.DropRateMul)).IsEqualTo(150d);
        }
        finally
        {
            UnitAttributeLimitGameData.Instance.ClearForTests();
        }
    }
}
