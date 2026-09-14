using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The conversion chain is rpack -members-> conv -members-> ppack -> products. The rows here mirror the
/// 10.0.2.13 layout, including the case the old loader got wrong: a reagent pack and a product pack that do
/// not share an id.
/// </summary>
public class ItemConversionGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE item_conv_sets (id INTEGER PRIMARY KEY, name TEXT, dialog_title TEXT, dialog_content TEXT);
            CREATE TABLE item_convs (id INTEGER PRIMARY KEY, name TEXT, item_conv_set_id INTEGER);
            CREATE TABLE item_conv_rpacks (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE item_conv_rpack_members (id INTEGER PRIMARY KEY, item_conv_id INTEGER NOT NULL, item_conv_rpack_id INTEGER NOT NULL);
            CREATE TABLE item_conv_reagents (id INTEGER PRIMARY KEY, item_conv_rpack_id INTEGER NOT NULL, item_id INTEGER NOT NULL, grade_id INTEGER DEFAULT 1, max_grade_id INTEGER NOT NULL);
            CREATE TABLE item_conv_reagent_filters (id INTEGER PRIMARY KEY, name TEXT, item_conv_rpack_id INTEGER, item_impl_id INTEGER NOT NULL, min_level INTEGER NOT NULL, max_level INTEGER NOT NULL, item_grade_id INTEGER NOT NULL, max_item_grade_id INTEGER NOT NULL, item_conv_epack_id INTEGER NOT NULL);
            CREATE TABLE item_conv_epacks (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE item_conv_exception_filters (id INTEGER PRIMARY KEY, item_category_id INTEGER NOT NULL, item_conv_epack_id INTEGER);
            CREATE TABLE item_conv_ppacks (id INTEGER PRIMARY KEY, name TEXT, chance_rate INTEGER NOT NULL);
            CREATE TABLE item_conv_ppack_members (id INTEGER PRIMARY KEY, item_conv_id INTEGER NOT NULL, item_conv_ppack_id INTEGER NOT NULL);
            CREATE TABLE item_conv_products (id INTEGER PRIMARY KEY, item_conv_ppack_id INTEGER NOT NULL, item_id INTEGER NOT NULL, weight INTEGER, min INTEGER, max INTEGER, item_grade_id INTEGER NOT NULL);
            """);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Filter-based evenstone reagent (filter 1: weapons, level 20-29, grade 2-3) whose reagent pack is 5 and
    /// whose product pack is 9, so the pack ids deliberately differ.
    /// </summary>
    private void SeedDisenchant()
    {
        Execute(
            """
            INSERT INTO item_conv_sets (id, name, dialog_title, dialog_content) VALUES (3, 'disenchant', 'Extract', 'Confirm?');
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (1, 'disenchant.weapon.uncommon~rare.20-29', 3);
            INSERT INTO item_conv_rpacks (id, name) VALUES (5, 'disenchant.weapon.uncommon~rare.20-29.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (1, 1, 5);
            INSERT INTO item_conv_reagent_filters (id, name, item_conv_rpack_id, item_impl_id, min_level, max_level, item_grade_id, max_item_grade_id, item_conv_epack_id)
                VALUES (1, 'disenchant.weapon.uncommon~rare.20-29', 5, 1, 20, 29, 2, 3, 0);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (9, 'disenchant.weapon.uncommon~rare.20-29.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (1, 1, 9);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (1, 9, 25798, 1, 2, 3, -1);
            """);
    }

    /// <summary>Explicit item reagent (pack 6) feeding conversion 2 in family 7, product pack 6.</summary>
    private void SeedExplicitReagent()
    {
        Execute(
            """
            INSERT INTO item_conv_sets (id, name, dialog_title, dialog_content) VALUES (7, 'awakening', 'Awaken', 'Confirm?');
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (2, 'awakening.ring', 7);
            INSERT INTO item_conv_rpacks (id, name) VALUES (6, 'awakening.ring.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (2, 2, 6);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (1, 6, 1459, 1, 3);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (6, 'awakening.ring.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (2, 2, 6);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (2, 6, 34983, 1, 1, 1, 5);
            """);
    }

    private void SeedExceptions()
    {
        Execute(
            """
            INSERT INTO item_conv_epacks (id, name) VALUES (2, 'test');
            INSERT INTO item_conv_exception_filters (id, item_category_id, item_conv_epack_id) VALUES (1, 173, 2);
            UPDATE item_conv_reagent_filters SET item_conv_epack_id = 2 WHERE id = 1;
            """);
    }

    [Test]
    public async Task ReagentPack_IsResolvedThroughTheFilterLadder()
    {
        SeedDisenchant();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(5u);
        await Assert.That(reagent.ConversionIds.Contains(1u)).IsTrue();
        // The family comes from item_convs.item_conv_set_id, which the old loader never filled.
        await Assert.That(reagent.ConversionSet).IsEqualTo(3u);
    }

    [Test]
    public async Task ReagentLookup_RejectsGradeAndLevelOutsideTheFilter()
    {
        SeedDisenchant();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        await Assert.That(data.GetReagentForItem(4, ItemImplEnum.Weapon, 12345, 25)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 30)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Armor, 12345, 25)).IsNull();
    }

    [Test]
    public async Task Product_IsReachedThroughTheConversionNotThroughMatchingPackIds()
    {
        SeedDisenchant();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var rolled = data.TryRollProduct(reagent, out var roll);

        await Assert.That(rolled).IsTrue();
        await Assert.That(roll.Product).IsNotNull();
        await Assert.That(roll.Product.OutputItemId).IsEqualTo(25798u);
        await Assert.That(roll.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(roll.Count).IsLessThanOrEqualTo(3);
    }

    [Test]
    public async Task ExplicitItemReagent_WinsOverAFilterAndCarriesItsOwnProduct()
    {
        SeedDisenchant();
        SeedExplicitReagent();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        // Item 1459 is grade 2, so it also satisfies the weapon filter (levels 20-29). The explicit row must win.
        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 1459, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(6u);
        await Assert.That(reagent.IsExplicitItem).IsTrue();
        await Assert.That(reagent.ConversionSet).IsEqualTo(7u);

        await Assert.That(data.TryRollProduct(reagent, out var roll)).IsTrue();
        await Assert.That(roll.Product.OutputItemId).IsEqualTo(34983u);
        await Assert.That(roll.Product.GradeId).IsEqualTo(5);
    }

    [Test]
    public async Task ConversionSet_IsValidatedAgainstTheEffectValue()
    {
        SeedDisenchant();
        SeedExplicitReagent();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var disenchant = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var awakening = data.GetReagentForItem(2, ItemImplEnum.Weapon, 1459, 25);

        await Assert.That(data.IsValidConversionSet(3, disenchant)).IsTrue();
        await Assert.That(data.IsValidConversionSet(7, disenchant)).IsFalse();
        await Assert.That(data.IsValidConversionSet(7, awakening)).IsTrue();
        await Assert.That(data.IsValidConversionSet(0, awakening)).IsFalse();
    }

    [Test]
    public async Task ExceptionPack_KeepsAnItemCategoryOutOfTheFilter()
    {
        SeedDisenchant();
        SeedExceptions();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25, 173)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25, 200)).IsNotNull();
    }

    [Test]
    public async Task FailedPackChance_StillCountsAsAConversion()
    {
        SeedDisenchant();
        Execute("UPDATE item_conv_ppacks SET chance_rate = 0 WHERE id = 9");
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        // The pack exists, so the roll happened and lost: the caller has to consume the reagent but grant
        // nothing. Distinguishing this from "no product rows at all" is what TryRollProduct's return means.
        await Assert.That(data.TryRollProduct(reagent, out var roll)).IsTrue();
        await Assert.That(roll.Product).IsNull();
        await Assert.That(roll.ChanceFailed).IsTrue();
        await Assert.That(roll.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReagentWithoutAnyProductRows_ReportsFailure()
    {
        SeedDisenchant();
        Execute("DELETE FROM item_conv_products");
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(data.TryRollProduct(reagent, out _)).IsFalse();
    }

    [Test]
    public async Task ReagentPackMissingFromTheMemberTable_FallsBackToItsOwnProductPackId()
    {
        // 41 of the 5519 reagent packs the content references (repackaging sockets, the 6-tier awakening
        // dagger, "disuse", the discard-only packs, two housing blueprints) have no item_conv_rpack_members
        // row at all and are only reachable because a product pack shares their id.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_rpacks (id, name) VALUES (11, 'repackage_socket_skyblue_1T.reagent');
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (2, 11, 7777, 1, 1);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (11, 'repackage_socket_skyblue_1T.product', 10000);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (3, 11, 8800, 1, 1, 1, -1);
            """);
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(1, ItemImplEnum.Misc, 7777, 1);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ConversionIds.Count).IsEqualTo(0);
        await Assert.That(data.TryRollProduct(reagent, out var roll)).IsTrue();
        await Assert.That(roll.Product.OutputItemId).IsEqualTo(8800u);
    }

    [Test]
    public async Task ProductWeights_SteerThePick()
    {
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (21, 9, 11111, 1, 1, 1, -1), (22, 9, 22222, 3, 1, 1, -1);
            DELETE FROM item_conv_products WHERE id = 1;
            """);
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var heavy = 0;
        var light = 0;
        for (var i = 0; i < 400; i++)
        {
            await Assert.That(data.TryRollProduct(reagent, out var roll)).IsTrue();
            if (roll.Product.OutputItemId == 22222u)
                heavy++;
            else if (roll.Product.OutputItemId == 11111u)
                light++;
        }

        await Assert.That(heavy + light).IsEqualTo(400);
        // p(heavy) = 0.75, so 400 trials landing at or below half is not a realistic outcome.
        await Assert.That(heavy).IsGreaterThan(light);
    }

    [Test]
    public async Task ConversionSetMetadata_IsExposedForTheDialog()
    {
        SeedDisenchant();
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var set = data.GetConversionSet(3);

        await Assert.That(set).IsNotNull();
        await Assert.That(set.Name).IsEqualTo("disenchant");
        await Assert.That(set.ConversionIds.Contains(1u)).IsTrue();
    }

    [Test]
    public async Task ConversionWithoutAFamily_IsLeftUnresolved()
    {
        SeedDisenchant();
        // 43 of the 6409 item_convs rows carry a NULL family; such a reagent must not validate against any
        // conversion set the effect can name.
        Execute("UPDATE item_convs SET item_conv_set_id = NULL WHERE id = 1");
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ConversionSet).IsEqualTo(0u);
        await Assert.That(data.IsValidConversionSet(3, reagent)).IsFalse();
        // The products are still reachable through the conversion, so the cast can still be carried out.
        await Assert.That(data.TryRollProduct(reagent, out var roll)).IsTrue();
        await Assert.That(roll.Product).IsNotNull();
    }
}
