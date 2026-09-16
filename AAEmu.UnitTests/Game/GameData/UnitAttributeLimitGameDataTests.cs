using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.GameData;

// UnitAttributeLimitGameData is a singleton whose table the clamp tests also swap, so these tests must
// not overlap with them.
[NotInParallel]
public class UnitAttributeLimitGameDataTests : SqliteTestBase
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

    private void SeedLimits(params (int Id, int AttributeId, long Minimum, long Maximum)[] rows)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "DELETE FROM unit_attribute_limits";
        command.ExecuteNonQuery();

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
    }

    [Test]
    public async Task Load_MapsEachRowToItsAttribute()
    {
        // The three rows below are the shipped 10.0.2.13 values.
        SeedLimits(
            (2, 10, -10000, 8000),
            (1, 74, -666, 2000),
            (49, 223, -1000, 0));

        var data = UnitAttributeLimitGameData.Instance;
        try
        {
            data.Load(Connection);

            var moveSpeed = data.GetLimit(UnitAttribute.MoveSpeedMul);
            await Assert.That(moveSpeed.HasValue).IsTrue();
            await Assert.That(moveSpeed.Value.Minimum).IsEqualTo(-10000L);
            await Assert.That(moveSpeed.Value.Maximum).IsEqualTo(8000L);

            await Assert.That(data.GetLimit(UnitAttribute.GlobalCooldownMul).Value.Maximum).IsEqualTo(2000L);
            await Assert.That(data.GetLimit(UnitAttribute.ItemEvolvingCostMul).Value.Maximum).IsEqualTo(0L);

            // No row means unbounded, which is every attribute but these.
            await Assert.That(data.GetLimit(UnitAttribute.Str).HasValue).IsFalse();
            await Assert.That(data.GetLimit(UnitAttribute.Mass).HasValue).IsFalse();
        }
        finally
        {
            data.ClearForTests();
        }
    }

    [Test]
    public async Task Load_KeepsTheRowForMeleeBlock()
    {
        // unit_attribute_limits row 8 bounds melee_block (id 21) even though enum_unit_attribute has no
        // row for 21. UnitAttribute.MeleeBlock exists for exactly this row, so nothing is reported.
        SeedLimits((8, 21, 0, 2000000000));

        var data = UnitAttributeLimitGameData.Instance;
        try
        {
            data.Load(Connection);

            await Assert.That(data.GetLimit(UnitAttribute.MeleeBlock).Value.Maximum).IsEqualTo(2000000000L);
            await Assert.That(UnitAttributeLoadRules.UnknownIds([21])).IsEmpty();
        }
        finally
        {
            data.ClearForTests();
        }
    }

    [Test]
    public async Task Load_KeepsARowWhoseIdHasNoMember()
    {
        // The row loads, so the loader's warning is the only sign that nothing can query it.
        SeedLimits((99, 10000, 0, 10));

        var data = UnitAttributeLimitGameData.Instance;
        try
        {
            data.Load(Connection);

            await Assert.That(data.GetLimit((UnitAttribute)10000).HasValue).IsTrue();
            await Assert.That(UnitAttributeLoadRules.UnknownIds([10000])).IsEquivalentTo(new List<uint> { 10000 });
        }
        finally
        {
            data.ClearForTests();
        }
    }

    [Test]
    public async Task ClearForTests_ForgetsEverything()
    {
        SeedLimits((2, 10, -10000, 8000));

        var data = UnitAttributeLimitGameData.Instance;
        data.Load(Connection);
        data.ClearForTests();

        await Assert.That(data.GetLimit(UnitAttribute.MoveSpeedMul).HasValue).IsFalse();
    }
}
