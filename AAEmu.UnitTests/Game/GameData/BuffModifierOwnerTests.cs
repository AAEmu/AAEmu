using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// <c>buff_modifiers</c> through <see cref="BuffGameData.Load"/>: 921 of the 1,057 rows are owned by a buff,
/// 115 by an item and 21 by an expedition buff grade, and the buff-id table takes only the buff rows.
/// </summary>/// <remarks>
/// These rows live in the <see cref="BuffGameData"/> singleton, so the class runs outside the parallel pool:
/// another class loading the same table would otherwise replace the fixture mid-assertion.
/// </remarks>
[NotInParallel]
public class BuffModifierOwnerTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE buff_modifiers (
                id INTEGER PRIMARY KEY,
                owner_id INTEGER NOT NULL,
                owner_type TEXT NOT NULL,
                buff_id INTEGER,
                tag_id INTEGER,
                buff_attribute_id INTEGER NOT NULL,
                unit_modifier_type_id INTEGER NOT NULL,
                value INTEGER NOT NULL,
                synergy TEXT,
                enable TEXT
            );
            CREATE TABLE buff_tolerances (
                id INTEGER PRIMARY KEY,
                buff_tag_id INTEGER NOT NULL,
                step_duration INTEGER NOT NULL,
                final_step_buff_id INTEGER,
                character_time_reduction INTEGER
            );
            CREATE TABLE buff_tolerance_steps (
                id INTEGER PRIMARY KEY,
                buff_tolerance_id INTEGER NOT NULL,
                hit_chance INTEGER NOT NULL,
                time_reduction INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private void InsertModifier(uint id, uint ownerId, string ownerType, uint buffId, int attribute, int value)
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO buff_modifiers (id, owner_id, owner_type, buff_id, tag_id, buff_attribute_id, " +
            "unit_modifier_type_id, value, synergy, enable) " +
            "VALUES (@id, @ownerId, @ownerType, @buffId, 0, @attribute, 1, @value, 'f', 't')";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@ownerType", ownerType);
        command.Parameters.AddWithValue("@buffId", buffId);
        command.Parameters.AddWithValue("@attribute", attribute);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task AnItemOwnedModifierIsNotGrantedByTheBuffSharingItsId()
    {
        // buff_modifiers 1140/1143-style rows: an item that lengthens a buff. Owner id 500 is also the id of
        // a buff in this fixture, which is exactly what used to make the item row apply to that buff.
        InsertModifier(1, 500, "Item", 245, (int)BuffAttribute.Duration, 400);
        InsertModifier(2, 500, "Buff", 245, (int)BuffAttribute.Duration, 100);
        InsertModifier(3, 501, "Buff", 245, (int)BuffAttribute.Duration, 50);

        BuffGameData.Instance.Load(Connection);

        var buffOwned = BuffGameData.Instance.GetModifiersForBuff(500);
        await Assert.That(buffOwned.Count).IsEqualTo(1);
        await Assert.That(buffOwned[0].OwnerType).IsEqualTo("Buff");
        await Assert.That(buffOwned[0].Value).IsEqualTo(100);

        // The item's own row is still loaded, under the item that owns it.
        var itemOwned = BuffGameData.Instance.GetItemModifiers(500);
        await Assert.That(itemOwned.Count).IsEqualTo(1);
        await Assert.That(itemOwned[0].Value).IsEqualTo(400);
        await Assert.That(BuffGameData.Instance.GetItemModifiers(501)).IsEmpty();

        // And the unrelated buff keeps its own row only.
        var other = BuffGameData.Instance.GetModifiersForBuff(501);
        await Assert.That(other.Count).IsEqualTo(1);
        await Assert.That(other[0].Value).IsEqualTo(50);
    }

    [Test]
    public async Task ExpeditionBuffGradeRowsAreFiledUnderTheirGrade()
    {
        InsertModifier(1, 10, "ExpeditionBuffGrade", 2385, (int)BuffAttribute.Duration, -20000);
        InsertModifier(2, 10, "Buff", 1, (int)BuffAttribute.InDuration, 20);

        BuffGameData.Instance.Load(Connection);

        await Assert.That(BuffGameData.Instance.GetModifiersForBuff(10).Count).IsEqualTo(1);
        await Assert.That(BuffGameData.Instance.GetModifiersForBuff(10)[0].OwnerType).IsEqualTo("Buff");

        var grade = BuffGameData.Instance.GetGradeModifiers(10);
        await Assert.That(grade.Count).IsEqualTo(1);
        await Assert.That(grade[0].OwnerType).IsEqualTo("ExpeditionBuffGrade");
        await Assert.That(grade[0].Value).IsEqualTo(-20000);
    }

    [Test]
    public async Task ExpeditionModifiersAreReplacedWhenPurchasedGradeChanges()
    {
        InsertModifier(1, 10, "ExpeditionBuffGrade", 2385, (int)BuffAttribute.Duration, -20000);
        InsertModifier(2, 11, "ExpeditionBuffGrade", 2385, (int)BuffAttribute.Duration, -40000);
        BuffGameData.Instance.Load(Connection);
        var cache = new BuffModifiers();
        var unrelated = new BuffModifier
        {
            Id = 99, BuffId = 2385, BuffAttribute = BuffAttribute.Duration,
            UnitModifierType = AAEmu.Game.Models.Game.Units.UnitModifierType.Value, Value = 500
        };
        cache.AddModifier(unrelated);

        cache.ReplaceExpeditionModifiers([10]);
        await Assert.That(cache.GetModifiersForBuffId(2385).Select(row => row.Value))
            .IsEquivalentTo([500, -20000]);

        cache.ReplaceExpeditionModifiers([11]);
        await Assert.That(cache.GetModifiersForBuffId(2385).Select(row => row.Value))
            .IsEquivalentTo([500, -40000]);

        cache.ReplaceExpeditionModifiers([]);
        await Assert.That(cache.GetModifiersForBuffId(2385).Single()).IsSameReferenceAs(unrelated);
    }

    [Test]
    public async Task AnUnknownOwnerTypeIsInert()
    {
        InsertModifier(1, 700, "Doodad", 245, (int)BuffAttribute.Duration, 10);

        BuffGameData.Instance.Load(Connection);

        await Assert.That(BuffGameData.Instance.GetModifiersForBuff(700)).IsEmpty();
        await Assert.That(BuffGameData.Instance.GetItemModifiers(700)).IsEmpty();
        await Assert.That(BuffGameData.Instance.GetGradeModifiers(700)).IsEmpty();
    }
}
