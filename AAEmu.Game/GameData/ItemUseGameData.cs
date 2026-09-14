using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>What an item does when the player uses it.</summary>
public sealed class ItemOpenPaper
{
    /// <summary><c>item_open_papers.book_page_id</c>; zero when the item opens a whole book.</summary>
    public uint BookPageId { get; init; }

    /// <summary><c>item_open_papers.book_id</c>; zero when the item opens a single page.</summary>
    public uint BookId { get; init; }
}

/// <summary>
/// Item-driven actions the server has to know about: which quest a starter item begins
/// (<c>item_accept_quests</c>), which craft a recipe item teaches (<c>item_recipes</c>) and which
/// book page an item opens (<c>item_open_papers</c>).
///
/// None of the three tables are read anywhere else in the server. The 10.0.2.13 client reads all three
/// from its own copy of the world database, so it renders the paper page itself and links a recipe item to
/// its craft locally; the server has to agree with it, and for quest starter items and recipe learning it
/// is the only side that can record the result.
/// </summary>
[GameData]
public class ItemUseGameData : Singleton<ItemUseGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Initialised empty rather than null so a query before Load() (tests, tooling) is a miss and not a crash.
    private Dictionary<uint, uint> _questByStarterItem = [];
    private Dictionary<uint, List<uint>> _craftsByRecipeItem = [];
    private HashSet<uint> _recipeGatedCrafts = [];
    private Dictionary<uint, ItemOpenPaper> _papersByItem = [];

    /// <summary>Rows skipped because they name neither a page nor a book (31 of 708 in 10.0.2.13).</summary>
    private int _papersWithoutContent;

    /// <summary>
    /// Quest an item starts, from <c>item_accept_quests</c>, or zero when the table has no usable row.
    /// 37 of the 814 rows carry <c>quest_id</c> 0 (hunting-request papers the quest itself consumes) and
    /// are dropped at load.
    /// </summary>
    public uint GetQuestIdForItem(uint itemTemplateId) =>
        _questByStarterItem.GetValueOrDefault(itemTemplateId);

    /// <summary>Crafts a recipe item teaches. Empty when the item is not a recipe.</summary>
    public IReadOnlyList<uint> GetCraftsForRecipeItem(uint itemTemplateId) =>
        _craftsByRecipeItem.TryGetValue(itemTemplateId, out var crafts) ? crafts : [];

    /// <summary>
    /// True when a craft is reachable only through a recipe item. Built from the self-consistent rows of
    /// <c>item_recipes</c>: 1421 of its 2822 rows name a craft that makes the item the recipe is for, and
    /// those cover 1421 of the 12402 crafts in 10.0.2.13.
    /// </summary>
    /// <remarks>
    /// Dead content on this client, which is why the crafting gate that reads this cannot refuse anything a
    /// player would otherwise do: nothing grants those recipe items and nothing offers those crafts. See the
    /// note in <see cref="CharacterCraft.Craft"/> for the counts.
    /// </remarks>
    public bool IsRecipeGatedCraft(uint craftId) => _recipeGatedCrafts.Contains(craftId);

    /// <summary>Paper an item opens, if it is an <c>impl_id</c> 23 (open_paper) item.</summary>
    public bool TryGetPaper(uint itemTemplateId, out ItemOpenPaper paper) =>
        _papersByItem.TryGetValue(itemTemplateId, out paper);

    public void Load(SqliteConnection connection)
    {
        _questByStarterItem = [];
        _craftsByRecipeItem = [];
        _recipeGatedCrafts = [];
        _papersByItem = [];
        _papersWithoutContent = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id, quest_id FROM item_accept_quests";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id");
                var questId = reader.GetUInt32("quest_id");
                if (itemId == 0 || questId == 0)
                    continue;

                if (!_questByStarterItem.TryAdd(itemId, questId))
                    Logger.Warn("item_accept_quests: item {0} names more than one quest; keeping {1}", itemId, _questByStarterItem[itemId]);
            }
        }

        LoadRecipes(connection);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id, book_page_id, book_id FROM item_open_papers";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id");
                if (itemId == 0)
                    continue;

                var pageId = reader.GetUInt32("book_page_id", 0);
                var bookId = reader.GetUInt32("book_id", 0);
                if (pageId == 0 && bookId == 0)
                {
                    _papersWithoutContent++;
                    continue;
                }

                _papersByItem[itemId] = new ItemOpenPaper { BookPageId = pageId, BookId = bookId };
            }
        }
    }

    /// <summary>
    /// Loads <c>item_recipes</c>, keeping only rows the content agrees with itself on.
    ///
    /// 1401 of the 2822 rows name a recipe item that does not describe the craft it points at: item 4183
    /// "Recipe: Nuia longsword" points at craft 2, which makes Solzreed strawberry jam, and the first rows
    /// run craft_id = id - 1 all the way down - the export enumerated two lists and paired them by position.
    /// Later rows do line up (9210 "Recipe: Chilling Waterfall of the Wild" -> craft 905 of the same name).
    /// A row is kept when the recipe item's name (minus any "label: " prefix) is the craft's title or one of
    /// its products. Taking the table at face value would burn the item and teach a craft it does not name,
    /// and - through the crafting gate - lock 1342 live crafts behind recipe items for removed content.
    ///
    /// If nothing corroborates (a database whose item and craft names are in a language this test cannot
    /// line up, or an empty content set), no craft is treated as recipe-gated: the gate fails open rather
    /// than locking the whole craft book.
    /// </summary>
    private void LoadRecipes(SqliteConnection connection)
    {
        var craftTitles = new Dictionary<uint, string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, title FROM crafts";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
                craftTitles[reader.GetUInt32("id")] = reader.GetString("title", string.Empty);
        }

        var craftProductNames = new Dictionary<uint, HashSet<string>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT cp.craft_id, i.name FROM craft_products cp JOIN items i ON i.id = cp.item_id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var craftId = reader.GetUInt32("craft_id");
                if (!craftProductNames.TryGetValue(craftId, out var names))
                {
                    names = [];
                    craftProductNames[craftId] = names;
                }

                names.Add(reader.GetString("name", string.Empty));
            }
        }

        var kept = 0;
        var selfContradictory = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT r.item_id, r.craft_id, i.name FROM item_recipes r LEFT JOIN items i ON i.id = r.item_id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id");
                var craftId = reader.GetUInt32("craft_id");
                if (itemId == 0 || craftId == 0)
                    continue;

                if (!NamesTheCraft(reader.GetString("name", string.Empty), craftId, craftTitles, craftProductNames))
                {
                    selfContradictory++;
                    continue;
                }

                kept++;
                if (!_craftsByRecipeItem.TryGetValue(itemId, out var crafts))
                {
                    crafts = [];
                    _craftsByRecipeItem[itemId] = crafts;
                }

                if (!crafts.Contains(craftId))
                    crafts.Add(craftId);

                _recipeGatedCrafts.Add(craftId);
            }
        }

        if (kept == 0)
        {
            Logger.Warn(
                "item_recipes: no row names the craft it points at, so nothing is treated as recipe-gated; check that items.name and crafts.title are populated for this content set");
            return;
        }

        Logger.Info(
            "item_recipes: {0} rows kept, {1} dropped as self-contradictory, {2} crafts gated on a recipe",
            kept, selfContradictory, _recipeGatedCrafts.Count);
    }

    private static bool NamesTheCraft(
        string recipeItemName,
        uint craftId,
        Dictionary<uint, string> craftTitles,
        Dictionary<uint, HashSet<string>> craftProductNames)
    {
        // Any "label: " prefix is dropped rather than matching a hardcoded Korean one, so the same rule
        // works whatever language the names are in.
        var target = recipeItemName;
        var separator = target.IndexOfAny([':', '\uFF1A']);
        if (separator >= 0)
            target = target[(separator + 1)..];
        target = target.Trim();
        if (target.Length == 0)
            return false;

        if (craftTitles.TryGetValue(craftId, out var title) && title == target)
            return true;

        return craftProductNames.TryGetValue(craftId, out var products) && products.Contains(target);
    }

    public void PostLoad()
    {
        // Deliberately no pruning against the live managers here: CraftManager is an ILoadable that the
        // orchestrator happens to run before PostLoadGameData(), but callers that drive game-data loading by
        // hand (integration tests) do not load it, and pruning against an empty craft table would empty
        // this loader. Callers validate instead - AcceptQuestEffect falls back to the quest-act search when
        // the row names a quest that no longer exists, and CharacterRecipeBook refuses unknown crafts.
        Logger.Info(
            "Item use data: {0} quest starter items, {1} recipe items, {2} recipe-gated crafts, {3} open-paper items ({4} rows name neither a page nor a book)",
            _questByStarterItem.Count, _craftsByRecipeItem.Count, _recipeGatedCrafts.Count, _papersByItem.Count,
            _papersWithoutContent);
    }
}
