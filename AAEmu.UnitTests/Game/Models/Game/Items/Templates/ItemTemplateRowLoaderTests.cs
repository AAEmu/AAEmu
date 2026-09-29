using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Templates;

/// <summary>
/// The item row projection is the only thing that reads the columns of the item template table, so
/// a column that ships content but is never read is a rule that silently does nothing. These tests
/// drive the real production projection over a synthetic table.
/// </summary>
/// <remarks>
/// The CREATE TABLE below is a schema stub, not a second loader: it holds no values and no logic, and
/// it cannot drift away from the projection unnoticed. The projection resolves every column by name
/// and the reader throws on a name the table does not have, so adding a read without adding the
/// column here fails these tests instead of passing them - that is what
/// <see cref="AMissingColumnStopsTheLoadInsteadOfReadingSomethingElse"/> pins.
/// </remarks>
public sealed class ItemTemplateRowLoaderTests
{
    private const string ItemsTable = """
        CREATE TABLE items (
            id INTEGER NOT NULL,
            name TEXT NULL,
            category_id INTEGER NOT NULL,
            level INTEGER NOT NULL,
            bind_id INTEGER NOT NULL,
            pickup_limit INTEGER NOT NULL,
            max_stack_size INTEGER NOT NULL,
            sellable TEXT NULL,
            use_skill_id INTEGER NOT NULL,
            use_skill_as_reagent TEXT NULL,
            impl_id INTEGER NOT NULL,
            buff_id INTEGER NOT NULL,
            gradable TEXT NULL,
            loot_multi TEXT NULL,
            loot_quest_id INTEGER NOT NULL,
            exp_abs_lifetime INTEGER NOT NULL,
            exp_online_lifetime INTEGER NOT NULL,
            exp_date TEXT NULL,
            expedition_level INTEGER NOT NULL,
            -- Shipped, and deliberately not projected: the rest of the item expiry family.
            -- See ItemExpiryDeferralTests for the pin on why.
            exp_day_of_week_id INTEGER NOT NULL,
            exp_day_of_week_min INTEGER NOT NULL,
            period_base_date TEXT NULL,
            one_time_sale TEXT NULL,
            limited_sale_count INTEGER NOT NULL,
            auction_only TEXT NULL,
            auto_loot TEXT NULL,
            proc_lifetime INTEGER NOT NULL,
            proc_recharge_restrict_item_id INTEGER NOT NULL,
            specialty_zone_id INTEGER NULL,
            level_requirement INTEGER NOT NULL,
            auction_a_category_id INTEGER NULL,
            auction_b_category_id INTEGER NULL,
            auction_c_category_id INTEGER NULL,
            level_limit INTEGER NOT NULL,
            fixed_grade INTEGER NOT NULL,
            max_enchantable_grade INTEGER NULL,
            disenchantable TEXT NULL,
            char_gender_id INTEGER NOT NULL,
            max_enchant_scale_id INTEGER NULL,
            auction_charge INTEGER NOT NULL,
            auction_charge_default TEXT NULL
        );
        """;

    [Test]
    public async Task EveryIgnoredFieldOfTheItemTableLandsOnTheTemplate()
    {
        // One row that sets every field this row covers to a value no default would produce, so a
        // projection that quietly skipped any of them would leave the default behind. The values are
        // positional and follow the CREATE TABLE order above.
        var template = Load(
            "INSERT INTO items VALUES (" +
            "4242, 'probe', 7, 11, 2, 3, 12," +          // id..max_stack_size
            " 't', 500, 't', 4, 600, 'f', 't', 900," +     // sellable..loot_quest_id
            " 1440, 30, '2027-06-01 00:00:00', 5, 5, 360," + // exp_abs..exp_day_of_week_min
            " '2023-01-01 06:00:00', 't', 10, 't', 't', 3, 45368," + // period_base..proc_recharge
            " 77, 21, 1, 2, 3, 41, -1, 7, 'f', 2, 9, 250, 'f');"); // specialty_zone..auction_charge_default

        await Assert.That(template.Id).IsEqualTo(4242u);
        await Assert.That(template.Name).IsEqualTo("probe");

        // lifetime
        await Assert.That(template.ExpAbsLifetime).IsEqualTo(1440);
        await Assert.That(template.ExpOnlineLifetime).IsEqualTo(30);
        await Assert.That(template.ExpDate).IsEqualTo(new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Unspecified));

        // expedition_level is set on this row on purpose and is deliberately NOT projected onto the
        // template. Reading it and gating item use on it was an inference the content does not
        // support: it is a different column from expedition_buffs.expedition_level_id, which is the
        // one the guild shop already gates purchases on. With no evidence for what the items column
        // governs, the loader leaves it alone rather than inventing a rule from its name.

        // The period and weekday columns are set on this row on purpose and are not asserted on
        // the template: the loader does not project them. ItemExpiryDeferralTests pins that.

        // sale columns
        await Assert.That(template.OneTimeSale).IsTrue();
        await Assert.That(template.LimitedSaleCount).IsEqualTo(10);
        await Assert.That(template.AuctionOnly).IsTrue();

        // loot column
        await Assert.That(template.AutoLoot).IsTrue();

        // proc columns
        await Assert.That(template.ProcLifetime).IsEqualTo(3);
        await Assert.That(template.ProcRechargeRestrictItemId).IsEqualTo(45368u);

        // the fields the row already carried, to prove the refactor did not lose any of them
        await Assert.That(template.Sellable).IsTrue();
        await Assert.That(template.UseSkillAsReagent).IsTrue();
        await Assert.That(template.Gradable).IsFalse();
        await Assert.That(template.LootMulti).IsTrue();
        await Assert.That(template.Disenchantable).IsFalse();
        await Assert.That(template.AuctionSettings.AuctionChargeDefault).IsFalse();
    }

    [Test]
    public async Task TheTextBooleansOfTheShippedTableAreNotCoercedToFalse()
    {
        // The shipped table stores these as 't'/'f' text and the generic reader maps anything that is
        // not one of the two to false. A row that says 't' has to come out true.
        var template = Load(
            "INSERT INTO items VALUES (" +
            "1, NULL, 0, 0, 0, 0, 1," +
            " 't', 0, 'f', 0, 0, 't', 'f', 0," +
            " 0, 0, NULL," +
            " 0, 8, 0," +
            " NULL," +
            " 't', 0," +
            " 't'," +
            " 't'," +
            " 0, 0," +
            " NULL, 0, NULL, NULL, NULL, 0, 0, NULL, 'f', 0, NULL, 0, 't');");

        await Assert.That(template.Name).IsEqualTo("");
        await Assert.That(template.Sellable).IsTrue();
        await Assert.That(template.Gradable).IsTrue();
        await Assert.That(template.LootMulti).IsFalse();
        await Assert.That(template.UseSkillAsReagent).IsFalse();
        await Assert.That(template.OneTimeSale).IsTrue();
        await Assert.That(template.AuctionOnly).IsTrue();
        await Assert.That(template.AutoLoot).IsTrue();
        await Assert.That(template.AuctionSettings.AuctionChargeDefault).IsTrue();
    }

    [Test]
    public async Task ABooleanTheBuildCannotReadStopsTheLoad()
    {
        // 't' and 'f' are the only two values the shipped table holds. Anything else is content this
        // build does not understand, and folding it into false would silently drop a limit.
        using var connection = CreateDb(ItemsTable,
            "INSERT INTO items VALUES (" +
            "1, NULL, 0, 0, 0, 0, 1," +
            " 'yes', 0, 'f', 0, 0, 't', 'f', 0," +
            " 0, 0, NULL," +
            " 0, 8, 0," +
            " NULL," +
            " 'f', 0," +
            " 'f'," +
            " 'f'," +
            " 0, 0," +
            " NULL, 0, NULL, NULL, NULL, 0, 0, NULL, 'f', 0, NULL, 0, 't');");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM items";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        await Assert.That(reader.Read()).IsTrue();
        var exception = Assert.Throws<InvalidDataException>(() =>
            ItemTemplateRowLoader.Apply(reader, new ItemTemplate()));
        await Assert.That(exception.Message).Contains("sellable");
    }

    [Test]
    public async Task ANullDateIsNoDateAndNotTheEpoch()
    {
        var template = Load(
            "INSERT INTO items VALUES (" +
            "1, NULL, 0, 0, 0, 0, 1," +
            " 't', 0, 'f', 0, 0, 't', 'f', 0," +
            " 0, 0, NULL," +
            " 0, 8, 0," +
            " NULL," +
            " 'f', 0," +
            " 'f'," +
            " 'f'," +
            " 0, 0," +
            " NULL, 0, NULL, NULL, NULL, 0, 0, NULL, 'f', 0, NULL, 0, 't');");

        await Assert.That(template.ExpDate).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task AMissingColumnStopsTheLoadInsteadOfReadingSomethingElse()
    {
        // The lock-step guard for the schema stub above: a projection that starts reading a column
        // the stub does not declare throws, so the stub and the projection cannot drift apart with
        // these tests still green.
        using var connection = CreateDb("CREATE TABLE items (id INTEGER NOT NULL, name TEXT NULL);",
            "INSERT INTO items VALUES (1, 'probe');");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM items";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        await Assert.That(reader.Read()).IsTrue();

        // The reader resolves a column by name and the provider throws for a name the table does not
        // have, so the projection cannot fall through to a neighbouring field.
        var thrown = Capture(() => ItemTemplateRowLoader.Apply(reader, new ItemTemplate()));

        await Assert.That(thrown).IsNotNull();
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static ItemTemplate Load(string insert)
    {
        using var connection = CreateDb(ItemsTable, insert);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM items";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        if (!reader.Read())
            throw new InvalidOperationException("The synthetic items row did not read back.");
        return ItemTemplateRowLoader.Apply(reader, new ItemTemplate());
    }

    private static SqliteConnection CreateDb(params string[] statements)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        foreach (var statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        return connection;
    }
}
