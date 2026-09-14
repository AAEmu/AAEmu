using AAEmu.Game.GameData;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The conversion chain is rpack -members-> conv -members-> ppack -> products. The rows here mirror the
/// 10.0.2.13 layout, including the cases the old loader got wrong: a reagent pack and a product pack that do
/// not share an id, a conversion that pays several packs at once, a reagent pack with no member at all, and
/// a pack whose conversions sit in two different families.
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

    private ItemConversionGameData Load()
    {
        var data = new ItemConversionGameData();
        data.Load(Connection);
        data.PostLoad();
        return data;
    }

    /// <summary>
    /// Filter-based evenstone reagent (filter 1: weapons, level 20-29, grade 2-3) whose reagent pack is 5 and
    /// whose product pack is 9, so the pack ids deliberately differ. Family 3 = disenchant.
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
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(5u);
        await Assert.That(reagent.ConversionIds.Contains(1u)).IsTrue();
        // The family comes from item_convs.item_conv_set_id, which the old loader never filled.
        await Assert.That(reagent.HasFamily(3)).IsTrue();
        await Assert.That(reagent.HasKnownFamily).IsTrue();
    }

    [Test]
    public async Task ReagentLookup_RejectsGradeAndLevelOutsideTheFilter()
    {
        SeedDisenchant();
        var data = Load();

        await Assert.That(data.GetReagentForItem(4, ItemImplEnum.Weapon, 12345, 25)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 30)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Armor, 12345, 25)).IsNull();
    }

    [Test]
    public async Task Product_IsReachedThroughTheConversionNotThroughMatchingPackIds()
    {
        SeedDisenchant();
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var rolled = data.TryRollProducts(reagent, 0, out var rolls);

        await Assert.That(rolled).IsTrue();
        await Assert.That(rolls.Count).IsEqualTo(1);
        await Assert.That(rolls[0].Product).IsNotNull();
        await Assert.That(rolls[0].Product.OutputItemId).IsEqualTo(25798u);
        await Assert.That(rolls[0].Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(rolls[0].Count).IsLessThanOrEqualTo(3);
    }

    [Test]
    public async Task ConversionWithTwoProductPacks_PaysBoth()
    {
        // Content shape of conversion 6280: two guaranteed packs, 1 housing blueprint and 50 enchanted
        // blueprints. Stopping at the first successful pack dropped the second.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (10, 'second.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (2, 1, 10);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (2, 10, 15596, 1, 50, 50, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var rolled = data.TryRollProducts(reagent, 3, out var rolls);

        await Assert.That(rolled).IsTrue();
        await Assert.That(rolls.Count).IsEqualTo(2);
        await Assert.That(rolls.Any(roll => roll.Product?.OutputItemId == 25798u)).IsTrue();
        await Assert.That(rolls.Any(roll => roll.Product?.OutputItemId == 15596u && roll.Count == 50)).IsTrue();
    }

    [Test]
    public async Task OnlyTheRequestedFamilyPaysOut()
    {
        // Content shape of reagent pack 2062: conversion 5740 is family 11 and pays 18 sealed Ipnir
        // enhancers, while conversion 2060 is a family-4 "dummy" paying 34 of an unrelated item. 125 reagent
        // packs feed more than one family, so a family-11 tool must not run the dummy chain.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_sets (id, name, dialog_title, dialog_content) VALUES (11, 'ipnir', '', '');
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (2060, 'dummy', 4), (5740, 'ipnir.armor', 11);
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (4, 'recycle_unused', NULL);
            INSERT INTO item_conv_rpacks (id, name) VALUES (2062, 'ipnir.armor.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (20, 5740, 2062), (21, 2060, 2062);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (30, 2062, 35658, 10, 10);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (5372, 'ipnir.product', 10000), (2133, 'dummy.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (30, 5740, 5372), (31, 2060, 2133);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (30, 5372, 46437, 1, 18, 18, -1), (31, 2133, 46185, 1, 34, 34, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(10, ItemImplEnum.Misc, 35658, 1, 0, 11);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.HasFamily(11)).IsTrue();
        await Assert.That(reagent.HasFamily(4)).IsTrue();

        await Assert.That(data.TryRollProducts(reagent, 11, out var rolls)).IsTrue();
        await Assert.That(rolls.Count).IsEqualTo(1);
        await Assert.That(rolls[0].Product.OutputItemId).IsEqualTo(46437u);
        await Assert.That(rolls[0].Count).IsEqualTo(18);

        // The family-4 dummy is the other family's business and must not pay.
        await Assert.That(data.TryRollProducts(reagent, 4, out var dummyRolls)).IsTrue();
        await Assert.That(dummyRolls.Single().Product.OutputItemId).IsEqualTo(46185u);
    }

    [Test]
    public async Task FamilylessConversions_StillPayWhenAFamilyIsRequested()
    {
        // Content shape of reagent packs 331-335: the origin-land armour socket disenchants cover 315 items
        // and their conversions carry a NULL item_conv_set_id, so there is no family to filter by.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (330, 'disenchant.originlandarmor.1.socket.uncommon', NULL);
            INSERT INTO item_conv_rpacks (id, name) VALUES (331, 'originlandarmor.socket.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (30, 330, 331);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (40, 331, 31000, 2, 2);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (331, 'originlandarmor.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (30, 330, 331);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (40, 331, 31011, 1, 1, 2, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Misc, 31000, 1, 0, 3);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(331u);
        await Assert.That(reagent.HasKnownFamily).IsFalse();
        // The effect allows a cast it cannot attribute, and the conversion still pays.
        await Assert.That(data.HasRoutesFor(reagent, 3)).IsTrue();
        await Assert.That(data.TryRollProducts(reagent, 3, out var rolls)).IsTrue();
        await Assert.That(rolls[0].Product.OutputItemId).IsEqualTo(31011u);
    }

    [Test]
    public async Task MixedKnownAndUnattributedPack_FallsBackToItsUnattributedRoute()
    {
        // Content shape of reagent pack 2725 (item 35938, the shotgun blueprint): the family-4 route is a
        // "dummy" paying 145 of item 46185, and the real route is the unattributed
        // discontinued_ship_paper.common paying 1 of item 46831. A request for any other family has to use
        // the unattributed route rather than reject the cast or roll both.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (2723, 'dummy', 4), (1304, 'discontinued_ship_paper.common', NULL);
            INSERT INTO item_conv_rpacks (id, name) VALUES (2725, 'ship_paper.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (40, 2723, 2725), (41, 1304, 2725);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (50, 2725, 35938, 0, 0);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (2796, 'dummy.product', 10000), (5488, 'ship_paper.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (40, 2723, 2796), (41, 1304, 5488);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (40, 2796, 46185, 1, 145, 145, -1), (41, 5488, 46831, 1, 1, 1, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(0, ItemImplEnum.Misc, 35938, 1, 0, 3);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(2725u);
        // Set 3 has no route of its own, so the cast is allowed through the unattributed one and the dummy
        // is not rolled.
        await Assert.That(data.HasRoutesFor(reagent, 3)).IsTrue();
        await Assert.That(data.TryRollProducts(reagent, 3, out var rolls)).IsTrue();
        await Assert.That(rolls.Single().Product.OutputItemId).IsEqualTo(46831u);

        // The family-4 route is still the one that pays when family 4 is asked for.
        await Assert.That(data.TryRollProducts(reagent, 4, out var dummyRolls)).IsTrue();
        await Assert.That(dummyRolls.Single().Product.OutputItemId).IsEqualTo(46185u);
    }

    [Test]
    public async Task PackWithOnlyOtherFamilies_HasNoRouteForTheRequest()
    {
        // The effect refuses the cast on this: reagent pack 97 carries family 4 and nothing unattributed.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_sets (id, name, dialog_title, dialog_content) VALUES (4, 'recycle', '', '');
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (97, 'recycle.weapon.sword', 4);
            INSERT INTO item_conv_rpacks (id, name) VALUES (97, 'recycle.weapon.sword.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (3, 97, 97);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (9, 97, 20191, 2, 11);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (97, 'recycle.weapon.sword.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (3, 97, 97);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (3, 97, 46185, 1, 1, 1, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 20191, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(data.HasRoutesFor(reagent, 3)).IsFalse();
        await Assert.That(data.HasRoutesFor(reagent, 4)).IsTrue();
        await Assert.That(data.TryRollProducts(reagent, 3, out var rolls)).IsFalse();
        await Assert.That(rolls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitItemReagent_WinsOverAFilterAndCarriesItsOwnProduct()
    {
        SeedDisenchant();
        SeedExplicitReagent();
        var data = Load();

        // Item 1459 is grade 2, so it also satisfies the weapon filter (levels 20-29). The explicit row must win.
        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 1459, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ReagentPackId).IsEqualTo(6u);
        await Assert.That(reagent.IsExplicitItem).IsTrue();
        await Assert.That(reagent.HasFamily(7)).IsTrue();

        await Assert.That(data.TryRollProducts(reagent, 0, out var rolls)).IsTrue();
        await Assert.That(rolls[0].Product.OutputItemId).IsEqualTo(34983u);
        await Assert.That(rolls[0].Product.GradeId).IsEqualTo(5);
    }

    [Test]
    public async Task ExplicitItemReagent_IsPassedOverWhenTheEffectAsksForAnotherFamily()
    {
        // Content shape of item 20191: an explicit row into pack 97 (family 4) and a filter into pack 3
        // (family 3). An evenstone asks for family 3, so the explicit row must not win.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_sets (id, name, dialog_title, dialog_content) VALUES (4, 'recycle', '', '');
            INSERT INTO item_convs (id, name, item_conv_set_id) VALUES (97, 'recycle.weapon.sword', 4);
            INSERT INTO item_conv_rpacks (id, name) VALUES (97, 'recycle.weapon.sword.reagent');
            INSERT INTO item_conv_rpack_members (id, item_conv_id, item_conv_rpack_id) VALUES (3, 97, 97);
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (9, 97, 20191, 2, 11);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (97, 'recycle.weapon.sword.product', 10000);
            INSERT INTO item_conv_ppack_members (id, item_conv_id, item_conv_ppack_id) VALUES (3, 97, 97);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (3, 97, 46185, 1, 1, 1, -1);
            """);
        var data = Load();

        // Item 20191 at grade 2 and level 25: explicit row -> pack 97 (family 4), filter -> pack 5 (family 3).
        var forDisenchant = data.GetReagentForItem(2, ItemImplEnum.Weapon, 20191, 25, 0, 3);
        await Assert.That(forDisenchant).IsNotNull();
        await Assert.That(forDisenchant.HasFamily(3)).IsTrue();

        // Without a requested family the explicit row still wins, and the effect's check then refuses it.
        var withoutFamily = data.GetReagentForItem(2, ItemImplEnum.Weapon, 20191, 25);
        await Assert.That(withoutFamily.ReagentPackId).IsEqualTo(97u);
        await Assert.That(data.IsValidConversionSet(3, withoutFamily)).IsFalse();
    }

    [Test]
    public async Task ConversionSet_IsValidatedAgainstTheEffectValue()
    {
        SeedDisenchant();
        SeedExplicitReagent();
        var data = Load();

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
        var data = Load();

        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25, 173)).IsNull();
        await Assert.That(data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25, 200)).IsNotNull();
    }

    [Test]
    public async Task FailedPackChance_StillCountsAsAConversion()
    {
        SeedDisenchant();
        Execute("UPDATE item_conv_ppacks SET chance_rate = 0 WHERE id = 9");
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        // The pack exists, so the roll happened and lost: the caller has to consume the reagent but grant
        // nothing. Distinguishing this from "no product rows at all" is what the return value means.
        await Assert.That(data.TryRollProducts(reagent, 0, out var rolls)).IsTrue();
        await Assert.That(rolls.Count).IsEqualTo(1);
        await Assert.That(rolls[0].Product).IsNull();
        await Assert.That(rolls[0].ChanceFailed).IsTrue();
        await Assert.That(rolls[0].Count).IsEqualTo(0);
    }

    [Test]
    public async Task ReagentWithoutAnyProductRows_ReportsFailure()
    {
        SeedDisenchant();
        Execute("DELETE FROM item_conv_products");
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(data.TryRollProducts(reagent, 0, out _)).IsFalse();
    }

    [Test]
    public async Task ReagentPackMissingFromTheMemberTable_RollsNothing()
    {
        // 41 of the 5519 reagent packs the content references have no item_conv_rpack_members row, and 42 of
        // them have an unrelated product pack whose id merely equals their own. Content shape of item 43580:
        // reagent pack 3759 (repackage_socket_skyblue_1T) against product pack 3759, an obsidian conversion
        // paying 16 of item 46185. Following the ids paid out the wrong item.
        SeedDisenchant();
        Execute(
            """
            INSERT INTO item_conv_rpacks (id, name) VALUES (11, 'repackage_socket_skyblue_1T.reagent');
            INSERT INTO item_conv_reagents (id, item_conv_rpack_id, item_id, grade_id, max_grade_id) VALUES (2, 11, 7777, 1, 1);
            INSERT INTO item_conv_ppacks (id, name, chance_rate) VALUES (11, 'discontinued_obsidian_2T_leather_chest_mythic', 10000);
            INSERT INTO item_conv_products (id, item_conv_ppack_id, item_id, weight, min, max, item_grade_id)
                VALUES (3, 11, 46185, 1, 16, 16, -1);
            """);
        var data = Load();

        var reagent = data.GetReagentForItem(1, ItemImplEnum.Misc, 7777, 1);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.ConversionIds.Count).IsEqualTo(0);
        await Assert.That(data.TryRollProducts(reagent, 0, out var rolls)).IsFalse();
        await Assert.That(rolls.Count).IsEqualTo(0);
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
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);
        var heavy = 0;
        var light = 0;
        for (var i = 0; i < 400; i++)
        {
            await Assert.That(data.TryRollProducts(reagent, 0, out var rolls)).IsTrue();
            if (rolls[0].Product.OutputItemId == 22222u)
                heavy++;
            else if (rolls[0].Product.OutputItemId == 11111u)
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
        var data = Load();

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
        var data = Load();

        var reagent = data.GetReagentForItem(2, ItemImplEnum.Weapon, 12345, 25);

        await Assert.That(reagent).IsNotNull();
        await Assert.That(reagent.HasKnownFamily).IsFalse();
        await Assert.That(data.IsValidConversionSet(3, reagent)).IsFalse();
        // The products are still reachable through the conversion, so the cast can still be carried out.
        await Assert.That(data.TryRollProducts(reagent, 3, out var rolls)).IsTrue();
        await Assert.That(rolls[0].Product).IsNotNull();
    }
}
