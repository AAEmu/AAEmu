using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The three tables behind item-driven actions. Values come from the 10.0.2.13 content so the tests pin the
/// shapes the loaders have to skip: quest 0 rows, a duplicated craft link, and an item that opens a book
/// rather than a page.
/// </summary>
public class ItemUseGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE item_accept_quests (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, quest_id INTEGER NOT NULL);
            CREATE TABLE item_recipes (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, craft_id INTEGER NOT NULL);
            CREATE TABLE item_open_papers (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, book_page_id INTEGER DEFAULT 0, book_id INTEGER DEFAULT 0);
            CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE crafts (id INTEGER PRIMARY KEY, title TEXT NOT NULL);
            CREATE TABLE craft_products (id INTEGER PRIMARY KEY, craft_id INTEGER NOT NULL, item_id INTEGER NOT NULL);
            """);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private ItemUseGameData Load()
    {
        var data = new ItemUseGameData();
        data.Load(Connection);
        data.PostLoad();
        return data;
    }

    [Test]
    public async Task QuestStarterItem_IsMappedFromTheTable()
    {
        Execute(
            """
            INSERT INTO item_accept_quests (id, item_id, quest_id) VALUES
                (16, 1459, 72),
                (19, 1536, 54);
            """);
        var data = Load();

        await Assert.That(data.GetQuestIdForItem(1459)).IsEqualTo(72u);
        await Assert.That(data.GetQuestIdForItem(1536)).IsEqualTo(54u);
        await Assert.That(data.GetQuestIdForItem(9999)).IsEqualTo(0u);
    }

    [Test]
    public async Task QuestStarterItem_QuestZeroRows_AreDropped()
    {
        // 37 of the 814 rows are hunting-request papers the quest itself consumes; accepting quest 0 is not
        // a thing, and the caller has to fall back to its own lookup.
        Execute(
            """
            INSERT INTO item_accept_quests (id, item_id, quest_id) VALUES
                (732, 44019, 0),
                (16, 1459, 72);
            """);
        var data = Load();

        await Assert.That(data.GetQuestIdForItem(44019)).IsEqualTo(0u);
        await Assert.That(data.GetQuestIdForItem(1459)).IsEqualTo(72u);
    }

    /// <summary>
    /// A recipe item the craft agrees with: item 9210 "Recipe: Chilling Waterfall of the Wild" (the client's
    /// zh_cn name for craft 905, whose product has the same name).
    /// </summary>
    private void SeedConsistentRecipe()
    {
        Execute(
            """
            INSERT INTO items (id, name) VALUES (9210, 'Recipe: Chilling Waterfall of the Wild'), (5955, 'Chilling Waterfall of the Wild');
            INSERT INTO crafts (id, title) VALUES (905, 'Chilling Waterfall of the Wild');
            INSERT INTO craft_products (id, craft_id, item_id) VALUES (1, 905, 5955);
            INSERT INTO item_recipes (id, item_id, craft_id) VALUES (900, 9210, 905);
            """);
    }

    [Test]
    public async Task RecipeItem_MapsToItsCraft()
    {
        SeedConsistentRecipe();
        var data = Load();

        await Assert.That(data.GetCraftsForRecipeItem(9210).Single()).IsEqualTo(905u);
        await Assert.That(data.GetCraftsForRecipeItem(1).Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecipeItem_NamesTheCraftThroughOneOfItsProducts()
    {
        // Item 9192 "Recipe: Trainee's two-handed blunt" is corroborated by the craft's product, not its title.
        Execute(
            """
            INSERT INTO items (id, name) VALUES (9192, 'Recipe: Trainee two-handed blunt'), (5937, 'Trainee two-handed blunt');
            INSERT INTO crafts (id, title) VALUES (887, 'Hunter destructive blunt');
            INSERT INTO craft_products (id, craft_id, item_id) VALUES (1, 887, 5937);
            INSERT INTO item_recipes (id, item_id, craft_id) VALUES (900, 9192, 887);
            """);
        var data = Load();

        await Assert.That(data.GetCraftsForRecipeItem(9192).Single()).IsEqualTo(887u);
        await Assert.That(data.IsRecipeGatedCraft(887)).IsTrue();
    }

    [Test]
    public async Task SelfContradictoryRecipeRow_IsDroppedAndGatesNothing()
    {
        // The real shape of the broken rows: item 4183 "Recipe: Nuia longsword" points at craft 2, which
        // makes Solzreed strawberry jam. 1401 of the 2822 content rows look like this.
        Execute(
            """
            INSERT INTO items (id, name) VALUES (4183, 'Recipe: Nuia longsword'), (24920, 'Solzreed strawberry jam');
            INSERT INTO crafts (id, title) VALUES (2, 'Specialty: Solzreed strawberry jam');
            INSERT INTO craft_products (id, craft_id, item_id) VALUES (1, 2, 24920);
            INSERT INTO item_recipes (id, item_id, craft_id) VALUES (3, 4183, 2);
            """);
        var data = Load();

        await Assert.That(data.GetCraftsForRecipeItem(4183).Count).IsEqualTo(0);
        await Assert.That(data.IsRecipeGatedCraft(2)).IsFalse();
    }

    [Test]
    public async Task WhenNoRowNamesItsCraft_NothingIsGated()
    {
        // A content set whose names cannot be lined up must not lock the whole craft book.
        Execute(
            """
            INSERT INTO items (id, name) VALUES (4183, 'untranslated a'), (24920, 'untranslated b');
            INSERT INTO crafts (id, title) VALUES (2, 'untranslated c');
            INSERT INTO craft_products (id, craft_id, item_id) VALUES (1, 2, 24920);
            INSERT INTO item_recipes (id, item_id, craft_id) VALUES (3, 4183, 2);
            """);
        var data = Load();

        await Assert.That(data.IsRecipeGatedCraft(2)).IsFalse();
        await Assert.That(data.GetCraftsForRecipeItem(4183).Count).IsEqualTo(0);
    }

    [Test]
    public async Task RecipeItem_SeveralCraftsForOneCraftRow_AreDeduplicated()
    {
        SeedConsistentRecipe();
        Execute("INSERT INTO item_recipes (id, item_id, craft_id) VALUES (901, 9210, 905)");
        var data = Load();

        await Assert.That(data.GetCraftsForRecipeItem(9210).Count).IsEqualTo(1);
        await Assert.That(data.IsRecipeGatedCraft(905)).IsTrue();
    }

    [Test]
    public async Task RecipeGatedCraft_IsRecognisedForTheCraftGate()
    {
        SeedConsistentRecipe();
        var data = Load();

        await Assert.That(data.IsRecipeGatedCraft(905)).IsTrue();
        await Assert.That(data.IsRecipeGatedCraft(906)).IsFalse();
    }

    [Test]
    public async Task OpenPaperItem_ResolvesAPageOrABook()
    {
        Execute(
            """
            INSERT INTO item_open_papers (id, item_id, book_page_id, book_id) VALUES
                (1, 19507, 1, 0),
                (531, 29236, 0, 10);
            """);
        var data = Load();

        await Assert.That(data.TryGetPaper(19507, out var page)).IsTrue();
        await Assert.That(page.BookPageId).IsEqualTo(1u);
        await Assert.That(page.BookId).IsEqualTo(0u);

        await Assert.That(data.TryGetPaper(29236, out var book)).IsTrue();
        await Assert.That(book.BookPageId).IsEqualTo(0u);
        await Assert.That(book.BookId).IsEqualTo(10u);

        await Assert.That(data.TryGetPaper(1, out _)).IsFalse();
    }

    [Test]
    public async Task OpenPaperItem_WithoutAPageOrBook_IsSkipped()
    {
        Execute("INSERT INTO item_open_papers (id, item_id, book_page_id, book_id) VALUES (1, 19507, 0, 0)");
        var data = Load();

        await Assert.That(data.TryGetPaper(19507, out _)).IsFalse();
    }
}
