using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The loaders that cast a content <c>unit_attribute_id</c> keep the rows and report the ids the enum
/// lacks. <c>DamageModifierGameData</c> is the one of them small enough to run end to end here; the
/// table it reads is the same <c>unit_modifiers</c> the buff, item, npc and slave loaders read.
/// </summary>
// Both tests load the DamageModifierGameData singleton, so they must not run alongside each other.
[NotInParallel]
public class UnitAttributeLoadWarningTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE unit_modifiers (
                id INTEGER PRIMARY KEY,
                owner_id INTEGER,
                owner_type TEXT,
                unit_attribute_id INTEGER NOT NULL,
                unit_modifier_type_id INTEGER NOT NULL,
                linear_level_bonus INTEGER NOT NULL DEFAULT 0,
                enable TEXT NOT NULL DEFAULT 't',
                value INTEGER NOT NULL DEFAULT 0,
                dynamic_value INTEGER NOT NULL DEFAULT 0
            );
            """;
        command.ExecuteNonQuery();
    }

    private void SeedModifier(long id, uint ownerId, string ownerType, uint attributeId, long value)
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO unit_modifiers (id, owner_id, owner_type, unit_attribute_id, unit_modifier_type_id, value) " +
            "VALUES (@id, @ownerId, @ownerType, @attributeId, 0, @value)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@ownerType", ownerType);
        command.Parameters.AddWithValue("@attributeId", attributeId);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task RowsForIdsTheEnumLacks_StillLoad_AndAreReported()
    {
        // 8 is Armor; 14 is the id two shipped Buff rows use and enum_unit_attribute has no name for.
        SeedModifier(1, 77, "DamageEffect", 8, 120);
        SeedModifier(2, 77, "DamageEffect", 14, 10000);
        SeedModifier(3, 78, "DamageEffect", 64, 300);

        DamageModifierGameData.Instance.Load(Connection);

        var modifiers = DamageModifierGameData.Instance.GetModifiersForBuff(77);
        await Assert.That(modifiers.Count).IsEqualTo(2);
        await Assert.That(modifiers.Select(modifier => modifier.Attribute))
            .IsEquivalentTo(new List<UnitAttribute> { UnitAttribute.Armor, (UnitAttribute)14 });

        // The loader validates the same ids it loaded, so its one warning line for this table names 14
        // and nothing else.
        var warningIds = UnitAttributeLoadRules.UnknownIds(modifiers.Select(modifier => (long)(uint)modifier.Attribute));
        await Assert.That(warningIds).IsEquivalentTo(new List<uint> { 14 });

        var warning = UnitAttributeLoadRules.Warning("unit_modifiers (owner_type='DamageEffect')", warningIds);
        await Assert.That(warning).Contains("14");
        await Assert.That(warning).Contains("DamageEffect");
    }

    [Test]
    public async Task ShippedDamageEffectRows_ReportNothing()
    {
        // Every unit_attribute_id the 10.0.2.13 DamageEffect rows use (19 of them) has a member now.
        uint[] shipped = [0, 16, 17, 25, 26, 51, 52, 53, 57, 77, 78, 82, 83, 86, 88, 142, 148, 184, 204];
        foreach (var (attributeId, index) in shipped.Select((attributeId, index) => (attributeId, index)))
            SeedModifier(index + 1, 200, "DamageEffect", attributeId, 10);

        DamageModifierGameData.Instance.Load(Connection);

        var modifiers = DamageModifierGameData.Instance.GetModifiersForBuff(200);
        await Assert.That(modifiers.Count).IsEqualTo(shipped.Length);
        await Assert.That(UnitAttributeLoadRules.UnknownIds(modifiers.Select(modifier => (long)(uint)modifier.Attribute)))
            .IsEmpty();
    }
}
