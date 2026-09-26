using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public sealed class ExpeditionBuffGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE expedition_buffs (
                id INTEGER PRIMARY KEY, name TEXT, display_order INTEGER,
                expedition_level_id INTEGER, active TEXT
            );
            CREATE TABLE expedition_buff_grades (
                id INTEGER PRIMARY KEY, expedition_buff_id INTEGER, grade INTEGER, desc TEXT,
                contribution INTEGER, item_id INTEGER, count INTEGER, expedition_level_id INTEGER,
                housing TEXT, summon_limit INTEGER, portal_point_limit INTEGER
            );
            CREATE TABLE unit_modifiers (
                id INTEGER PRIMARY KEY, owner_id INTEGER, owner_type TEXT, unit_attribute_id INTEGER,
                unit_modifier_type_id INTEGER, value INTEGER, enable TEXT
            );
            INSERT INTO expedition_buffs VALUES (1, 'PvE', 1, 1, 't');
            INSERT INTO expedition_buff_grades VALUES (101, 1, 1, 'PvE damage +1%', 10, 0, 0, 1, 'f', NULL, NULL);
            INSERT INTO expedition_buff_grades VALUES (102, 1, 2, 'capacity', 20, 0, 0, 1, 'f', 2, 3);
            INSERT INTO unit_modifiers VALUES (1, 101, 'ExpeditionBuffGrade', 196, 0, 10, 't');
            INSERT INTO unit_modifiers VALUES (2, 101, 'Buff', 33, 0, 999, 't');
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task LoadUsesGradeOwnedEffectsAndCapacityBenefits()
    {
        ExpeditionBuffGameData.Instance.Load(Connection);

        var effects = ExpeditionBuffGameData.Instance.GetBonusEffects(1, 1).ToArray();
        await Assert.That(effects.Length).IsEqualTo(1);
        await Assert.That(effects[0].Attribute).IsEqualTo(UnitAttribute.MeleeDamageMulAntiNpc);
        await Assert.That(effects[0].ModifierType).IsEqualTo(UnitModifierType.Value);
        await Assert.That(effects[0].Value).IsEqualTo(10L);

        var capacity = ExpeditionBuffGameData.Instance.GetGrade(1, 2);
        await Assert.That(capacity.SummonLimit).IsEqualTo(2);
        await Assert.That(capacity.PortalPointLimit).IsEqualTo(3);
    }

    [Test]
    public async Task PostLoadAcceptsTheAuthoredGradeSequence()
    {
        ExpeditionBuffGameData.Instance.Load(Connection);
        ExpeditionBuffGameData.Instance.PostLoad();

        await Assert.That(ExpeditionBuffGameData.Instance.ContentDiagnostics).IsEmpty();
    }

    [Test]
    public async Task PostLoadRejectsAnOrphanEffectOwner()
    {
        using var command = Connection.CreateCommand();
        command.CommandText =
            "INSERT INTO unit_modifiers VALUES (3, 999, 'ExpeditionBuffGrade', 196, 0, 10, 't')";
        command.ExecuteNonQuery();

        ExpeditionBuffGameData.Instance.Load(Connection);
        var exception = Assert.Throws<InvalidOperationException>(() => ExpeditionBuffGameData.Instance.PostLoad());

        await Assert.That(exception.Message).Contains("owner 999");
    }

    [Test]
    public async Task ExpeditionSumsPurchasedCapacityBenefits()
    {
        ExpeditionBuffGameData.Instance.Load(Connection);
        var expedition = new Expedition { PurchasedBuffGrades = { [1] = 2 } };

        await Assert.That(expedition.GetSummonLimitBonus()).IsEqualTo(2);
        await Assert.That(expedition.GetPortalPointLimitBonus()).IsEqualTo(3);
    }
}
