using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CraftManager : Singleton<CraftManager>, ICraftManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Craft> _crafts;
    private Dictionary<uint, HashSet<uint>> _craftsByPack;
    private Dictionary<uint, CraftPack> _craftPacks;
    private Dictionary<uint, CraftLine> _craftLines;
    private Dictionary<uint, CraftLineComponent> _craftLineComponents;
    private Dictionary<uint, HashSet<uint>> _craftsByLine;
    private Dictionary<CraftCategoryLevel, Dictionary<uint, CraftCategory>> _craftCategories;
    private Dictionary<(CraftCategoryLevel Level, uint CategoryId), HashSet<uint>> _craftsByCategory;
    private HashSet<uint> _unresolvedCraftPackIds;
    private HashSet<uint> _unresolvedProductPackIds;
    private List<CraftCategoryMismatch> _craftCategoryMismatches;

    /// <summary>Orderable crafts by the item they produce, for the craft order board.</summary>
    private Dictionary<uint, uint> _orderableCraftsByProduct;

    public void Load()
    {
        _crafts = [];
        _craftsByPack = [];
        _craftPacks = [];
        _craftLines = [];
        _craftLineComponents = [];
        _craftsByLine = [];
        _craftCategories = new()
        {
            [CraftCategoryLevel.A] = [],
            [CraftCategoryLevel.B] = [],
            [CraftCategoryLevel.C] = [],
            [CraftCategoryLevel.D] = []
        };
        _craftsByCategory = [];
        _unresolvedCraftPackIds = [];
        _unresolvedProductPackIds = [];
        _craftCategoryMismatches = [];
        Logger.Info("Loading crafts...");

        using (var connection = SQLite.CreateConnection())
        {
            LoadCrafts(connection);
            /* Craft products (item you get at the end) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_products";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftProduct
                        {
                            Id = reader.GetUInt32("id"),
                            CraftId = craftId,
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount", 1),
                            Rate = reader.GetInt32("rate"),
                            // 10.0.2.13: show_lower_crafts removed
                            UseGrade = reader.GetBoolean("use_grade"),
                            ItemGradeId = reader.GetUInt32("item_grade_id")
                        };

                        _crafts[template.CraftId].CraftProducts.Add(template);
                    }
                }
            }

            /* Craft materials (items consumed by the craft) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_materials";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftMaterial
                        {
                            Id = reader.GetUInt32("id"),
                            CraftId = craftId,
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt32("amount", 1),
                            MainGrade = reader.GetBoolean("main_grade")
                        };

                        _crafts[craftId].CraftMaterials.Add(template);
                    }
                }
            }

            LoadCraftMetadata(connection);
            if (_unresolvedProductPackIds.Count > 0)
                Logger.Warn("Loaded {0} opaque products_pack_id references; the shipped catalog has no product-pack table.", _unresolvedProductPackIds.Count);
            if (_craftCategoryMismatches.Count > 0)
                Logger.Warn("Loaded {0} craft rows whose C category differs from their D category parent; both references were retained.", _craftCategoryMismatches.Count);
            LoadCraftPackMembership(connection);
            LoadActabilityGroups(connection);
            LoadOrderableProducts();
        }

        Logger.Info(
            "Loaded {0} crafts, {1} lines, {2} line components, {3} category rows, {4} packs ({5} unresolved pack ids, {6} opaque product-pack references)",
            _crafts.Count,
            _craftLines.Count,
            _craftLineComponents.Count,
            _craftCategories.Values.Sum(categories => categories.Count),
            _craftPacks.Count,
            _unresolvedCraftPackIds.Count,
            _unresolvedProductPackIds.Count);
    }

    internal void LoadCrafts(SqliteConnection connection)
    {
        _crafts = [];
        _unresolvedProductPackIds = [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM crafts";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            if (id == 0)
                throw new InvalidDataException("crafts contains a zero id.");
            if (_crafts.ContainsKey(id))
                throw new InvalidDataException($"crafts contains duplicate id {id}.");

            var productPackId = reader.IsDBNull("products_pack_id")
                ? (uint?)null
                : reader.GetUInt32("products_pack_id");
            var template = new Craft
            {
                Id = id,
                CastDelay = reader.GetInt32("cast_delay", 0),
                // 10.0.2.13: tool_id removed
                SkillId = reader.GetUInt32("skill_id", 0),
                WiId = reader.GetUInt32("wi_id", 0),
                MilestoneId = reader.GetUInt32("milestone_id", 0),
                ReqDoodadId = reader.GetUInt32("req_doodad_id", 0),
                // 10.0.2.13: need_bind, ac_id removed
                ActabilityLimit = reader.GetInt32("actability_limit", 0),
                // 10.0.2.13: show_upper_crafts removed
                RecommendLevel = reader.GetInt32("recommend_level", 0),
                VisibleOrder = reader.GetInt32("visible_order", 0),
                Orderable = reader.GetBoolean("orderable"),
                Cost = reader.GetInt32("cost", 0),
                UseOnlyActability = reader.GetBoolean("use_only_actability"),
                ProductPackId = productPackId == 0 ? null : productPackId,
                CraftCCategoryId = reader.IsDBNull("craft_c_category_id")
                    ? null
                    : reader.GetUInt32("craft_c_category_id"),
                CraftDCategoryId = reader.IsDBNull("craft_d_category_id")
                    ? null
                    : reader.GetUInt32("craft_d_category_id")
            };
            _crafts.Add(template.Id, template);
            if (template.ProductPackId is not null)
                _unresolvedProductPackIds.Add(template.ProductPackId.Value);
        }
    }

    /// <summary>
    /// Loads the category, line/component, and pack metadata which is joined to the craft rows.
    /// The compact catalog has no product-pack table; nonzero products_pack_id values are therefore
    /// retained as opaque references and reported instead of being joined to a guessed table.
    /// </summary>
    internal void LoadCraftMetadata(SqliteConnection connection)
    {
        _craftPacks = [];
        _craftLines = [];
        _craftLineComponents = [];
        _craftsByLine = [];
        _craftCategories = new()
        {
            [CraftCategoryLevel.A] = [],
            [CraftCategoryLevel.B] = [],
            [CraftCategoryLevel.C] = [],
            [CraftCategoryLevel.D] = []
        };
        _craftsByCategory = [];
        _unresolvedCraftPackIds = [];
        _unresolvedProductPackIds ??= [];
        _craftCategoryMismatches = [];
        foreach (var craft in _crafts.Values)
        {
            if (craft.ProductPackId is { } productPackId && productPackId != 0)
                _unresolvedProductPackIds.Add(productPackId);
        }

        LoadCraftCategories(connection);
        LoadCraftLines(connection);
        LoadCraftPacks(connection);
        ValidateCraftMetadataReferences();
        BuildCraftCategoryIndexes();
    }

    private void LoadCraftCategories(SqliteConnection connection)
    {
        LoadCraftCategoryRows(
            connection,
            CraftCategoryLevel.A,
            "craft_a_categories",
            "SELECT id, name, ui_order, btn_deco_key, child_file_path, represented_child_count, visible FROM craft_a_categories ORDER BY ui_order, id");
        LoadCraftCategoryRows(
            connection,
            CraftCategoryLevel.B,
            "craft_b_categories",
            "SELECT id, name, ui_order, craft_a_category_id, btn_deco_key, desc FROM craft_b_categories ORDER BY ui_order, id");
        LoadCraftCategoryRows(
            connection,
            CraftCategoryLevel.C,
            "craft_c_categories",
            "SELECT id, name, ui_order, craft_b_category_id, use_only_doodad FROM craft_c_categories ORDER BY ui_order, id");
        LoadCraftCategoryRows(
            connection,
            CraftCategoryLevel.D,
            "craft_d_categories",
            "SELECT id, name, ui_order, craft_c_category_id, use_only_doodad FROM craft_d_categories ORDER BY ui_order, id");
    }

    private void LoadCraftCategoryRows(
        SqliteConnection connection,
        CraftCategoryLevel level,
        string table,
        string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var categories = _craftCategories[level];
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            if (id == 0)
                throw new InvalidDataException($"{table} contains a zero id.");
            if (categories.ContainsKey(id))
                throw new InvalidDataException($"{table} contains duplicate id {id}.");

            var name = ReadRequiredString(reader, "name", table);
            var uiOrder = reader.GetInt32("ui_order");
            if (uiOrder < 0)
                throw new InvalidDataException($"{table} row {id} has a negative ui_order.");

            var category = new CraftCategory
            {
                Id = id,
                Level = level,
                Name = name,
                UiOrder = uiOrder,
                ParentId = level switch
                {
                    CraftCategoryLevel.B when !reader.IsDBNull("craft_a_category_id") => reader.GetUInt32("craft_a_category_id"),
                    CraftCategoryLevel.C when !reader.IsDBNull("craft_b_category_id") => reader.GetUInt32("craft_b_category_id"),
                    CraftCategoryLevel.D when !reader.IsDBNull("craft_c_category_id") => reader.GetUInt32("craft_c_category_id"),
                    _ => null
                },
                ButtonDecoKey = level is CraftCategoryLevel.A or CraftCategoryLevel.B && !reader.IsDBNull("btn_deco_key")
                    ? reader.GetString("btn_deco_key")
                    : null,
                ChildFilePath = level == CraftCategoryLevel.A && !reader.IsDBNull("child_file_path")
                    ? reader.GetString("child_file_path")
                    : null,
                RepresentedChildCount = level == CraftCategoryLevel.A && !reader.IsDBNull("represented_child_count")
                    ? reader.GetInt32("represented_child_count")
                    : null,
                Visible = level == CraftCategoryLevel.A && !reader.IsDBNull("visible") ? reader.GetBoolean("visible") : null,
                UseOnlyDoodad = level is CraftCategoryLevel.C or CraftCategoryLevel.D && !reader.IsDBNull("use_only_doodad")
                    ? reader.GetBoolean("use_only_doodad")
                    : null,
                Description = level == CraftCategoryLevel.B && !reader.IsDBNull("desc") ? reader.GetString("desc") : null
            };

            if (category.ParentId == 0)
                throw new InvalidDataException($"{table} row {id} has a zero parent id.");
            categories.Add(id, category);
        }

        ValidateCategoryParents(level, table);
    }

    private void ValidateCategoryParents(CraftCategoryLevel level, string table)
    {
        if (level == CraftCategoryLevel.A)
            return;

        var parentLevel = level switch
        {
            CraftCategoryLevel.B => CraftCategoryLevel.A,
            CraftCategoryLevel.C => CraftCategoryLevel.B,
            CraftCategoryLevel.D => CraftCategoryLevel.C,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };
        var parents = _craftCategories[parentLevel];
        foreach (var category in _craftCategories[level].Values)
        {
            if (category.ParentId is not { } parentId || !parents.TryGetValue(parentId, out var parent))
                throw new InvalidDataException($"{table} row {category.Id} has missing parent {category.ParentId} at level {parentLevel}.");
            if (parent.ChildIds.Contains(category.Id) == false)
                parent.ChildIds.Add(category.Id);
        }
    }

    private void LoadCraftLines(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, desc FROM craft_lines ORDER BY id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                if (id == 0)
                    throw new InvalidDataException("craft_lines contains a zero id.");
                if (_craftLines.ContainsKey(id))
                    throw new InvalidDataException($"craft_lines contains duplicate id {id}.");
                _craftLines.Add(id, new CraftLine
                {
                    Id = id,
                    Name = ReadRequiredString(reader, "name", "craft_lines"),
                    Description = ReadRequiredString(reader, "desc", "craft_lines")
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT c.id, c.craft_id, c.craft_line_id, c.rank, cr.id AS craft_row_id, l.id AS line_row_id " +
                "FROM craft_line_components c " +
                "LEFT JOIN crafts cr ON cr.id = c.craft_id " +
                "LEFT JOIN craft_lines l ON l.id = c.craft_line_id " +
                "ORDER BY c.craft_id, c.rank, c.id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            var ranks = new HashSet<(uint CraftId, uint Rank)>();
            var lineRanks = new HashSet<(uint LineId, uint Rank)>();
            while (reader.Read())
            {
                var id = reader.GetUInt32("id");
                var craftId = reader.GetUInt32("craft_id");
                var lineId = reader.GetUInt32("craft_line_id");
                var rank = reader.GetUInt32("rank");
                if (id == 0 || craftId == 0 || lineId == 0 || rank == 0)
                    throw new InvalidDataException($"craft_line_components row {id} contains a zero key or rank.");
                if (!_craftLineComponents.TryAdd(id, new CraftLineComponent
                    {
                        Id = id,
                        CraftId = craftId,
                        CraftLineId = lineId,
                        Rank = rank
                    }))
                    throw new InvalidDataException($"craft_line_components contains duplicate id {id}.");
                if (!ranks.Add((craftId, rank)))
                    throw new InvalidDataException($"craft_line_components has duplicate rank {rank} for craft {craftId}.");
                if (!lineRanks.Add((lineId, rank)))
                    throw new InvalidDataException($"craft_line_components has duplicate rank {rank} for line {lineId}.");
                if (reader.IsDBNull("craft_row_id") || !_crafts.ContainsKey(craftId))
                    throw new InvalidDataException($"craft_line_components row {id} references missing craft {craftId}.");
                if (reader.IsDBNull("line_row_id") || !_craftLines.TryGetValue(lineId, out var line))
                    throw new InvalidDataException($"craft_line_components row {id} references missing craft line {lineId}.");

                line.Components.Add(_craftLineComponents[id]);
                _crafts[craftId].CraftLineComponents.Add(_craftLineComponents[id]);
                if (!_craftsByLine.TryGetValue(lineId, out var craftIds))
                {
                    craftIds = [];
                    _craftsByLine.Add(lineId, craftIds);
                }
                craftIds.Add(craftId);
            }
        }

        foreach (var line in _craftLines.Values)
            line.Components.Sort((left, right) => left.Rank.CompareTo(right.Rank));
    }

    private void LoadCraftPacks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM craft_packs ORDER BY id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            if (id == 0)
                throw new InvalidDataException("craft_packs contains a zero id.");
            if (_craftPacks.ContainsKey(id))
                throw new InvalidDataException($"craft_packs contains duplicate id {id}.");
            _craftPacks.Add(id, new CraftPack
            {
                Id = id,
                Name = ReadRequiredString(reader, "name", "craft_packs")
            });
        }
    }

    private void ValidateCraftMetadataReferences()
    {
        foreach (var craft in _crafts.Values)
        {
            if (craft.CraftCCategoryId is { } cCategoryId && !_craftCategories[CraftCategoryLevel.C].ContainsKey(cCategoryId))
                throw new InvalidDataException($"crafts row {craft.Id} references missing C category {cCategoryId}.");
            if (craft.CraftDCategoryId is { } dCategoryId)
            {
                if (!_craftCategories[CraftCategoryLevel.D].TryGetValue(dCategoryId, out var dCategory))
                    throw new InvalidDataException($"crafts row {craft.Id} references missing D category {dCategoryId}.");
                if (craft.CraftCCategoryId is { } craftCCategoryId && dCategory.ParentId is { } dParentId && dParentId != craftCCategoryId)
                    _craftCategoryMismatches.Add(new CraftCategoryMismatch(craft.Id, craftCCategoryId, dCategoryId, dParentId));
            }
        }
    }

    private void BuildCraftCategoryIndexes()
    {
        foreach (var craft in _crafts.Values)
        {
            if (craft.CraftCCategoryId is not { } cCategoryId)
                continue;
            AddCraftToCategory(CraftCategoryLevel.C, cCategoryId, craft.Id);
            var cCategory = _craftCategories[CraftCategoryLevel.C][cCategoryId];
            if (cCategory.ParentId is not { } bCategoryId ||
                !_craftCategories[CraftCategoryLevel.B].TryGetValue(bCategoryId, out var bCategory))
                throw new InvalidDataException($"C category {cCategoryId} has no valid B parent.");
            AddCraftToCategory(CraftCategoryLevel.B, bCategoryId, craft.Id);
            if (bCategory.ParentId is not { } aCategoryId ||
                !_craftCategories[CraftCategoryLevel.A].TryGetValue(aCategoryId, out var aCategory))
                throw new InvalidDataException($"B category {bCategoryId} has no valid A parent.");
            AddCraftToCategory(CraftCategoryLevel.A, aCategoryId, craft.Id);

            if (craft.CraftDCategoryId is not { } dCategoryId)
                continue;
            AddCraftToCategory(CraftCategoryLevel.D, dCategoryId, craft.Id);
        }
    }

    private void AddCraftToCategory(CraftCategoryLevel level, uint categoryId, uint craftId)
    {
        var key = (level, categoryId);
        if (!_craftsByCategory.TryGetValue(key, out var craftIds))
        {
            craftIds = [];
            _craftsByCategory.Add(key, craftIds);
        }
        craftIds.Add(craftId);
    }

    private static string ReadRequiredString(SQLiteWrapperReader reader, string column, string table)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"{table}.{column} is null.");
        var value = reader.GetString(column);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{table}.{column} is empty.");
        return value;
    }

    /// <summary>
    /// Fills in the actability group of every craft that has one, which is what the craft order
    /// board filters a search by. The group lives on the craft's skill, not on the craft row.
    /// </summary>
    internal void LoadActabilityGroups(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.id AS craft_id, s.actability_group_id AS group_id FROM crafts c " +
            "JOIN skills s ON s.id = c.skill_id WHERE s.actability_group_id IS NOT NULL";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var craftId = reader.GetUInt32("craft_id");
            if (_crafts.TryGetValue(craftId, out var craft))
                craft.ActabilityGroupId = reader.GetUInt32("group_id", 0);
        }
    }

    /// <summary>
    /// Indexes the orderable crafts by the item they produce, so a board request that names an item
    /// resolves to its craft without a scan. When several orderable crafts make the same item the
    /// lowest craft id wins, which keeps the answer stable.
    /// </summary>
    internal void LoadOrderableProducts()
    {
        _orderableCraftsByProduct = [];
        foreach (var craft in _crafts.Values.Where(craft => craft.Orderable).OrderBy(craft => craft.Id))
        {
            foreach (var product in craft.CraftProducts)
            {
                if (!_orderableCraftsByProduct.ContainsKey(product.ItemId))
                    _orderableCraftsByProduct.Add(product.ItemId, craft.Id);
            }
        }
    }

    /// <summary>Resolves the orderable craft that produces an item, if the content has one.</summary>
    public bool TryFindOrderableCraftByProduct(uint itemId, out Craft craft)
    {
        craft = null;
        return _orderableCraftsByProduct != null &&
               _orderableCraftsByProduct.TryGetValue(itemId, out var craftId) &&
               _crafts.TryGetValue(craftId, out craft);
    }

    public Craft GetCraftById(uint craftId)
    {
        return _crafts[craftId];
    }

    public bool HasCraft(uint craftId)
    {
        return _crafts.ContainsKey(craftId);
    }

    public bool TryGetCraft(uint craftId, out Craft craft)
    {
        craft = null;
        return _crafts != null && _crafts.TryGetValue(craftId, out craft);
    }

    public bool IsCraftInPack(uint craftPackId, uint craftId)
    {
        return _craftsByPack != null &&
               _craftsByPack.TryGetValue(craftPackId, out var craftIds) &&
               craftIds.Contains(craftId);
    }

    public IReadOnlyCollection<uint> GetCraftIdsForPack(uint craftPackId)
    {
        return _craftsByPack != null && _craftsByPack.TryGetValue(craftPackId, out var craftIds)
            ? craftIds
            : Array.Empty<uint>();
    }

    public bool TryGetCraftLine(uint craftLineId, out CraftLine craftLine)
    {
        craftLine = null;
        return _craftLines != null && _craftLines.TryGetValue(craftLineId, out craftLine);
    }

    public IReadOnlyCollection<uint> GetCraftIdsForLine(uint craftLineId)
    {
        return _craftsByLine != null && _craftsByLine.TryGetValue(craftLineId, out var craftIds)
            ? craftIds
            : Array.Empty<uint>();
    }

    public bool TryGetCraftCategory(CraftCategoryLevel level, uint categoryId, out CraftCategory category)
    {
        category = null;
        return _craftCategories != null &&
               _craftCategories.TryGetValue(level, out var categories) &&
               categories.TryGetValue(categoryId, out category);
    }

    public IReadOnlyCollection<uint> GetCraftIdsForCategory(CraftCategoryLevel level, uint categoryId)
    {
        return _craftsByCategory != null &&
               _craftsByCategory.TryGetValue((level, categoryId), out var craftIds)
            ? craftIds
            : Array.Empty<uint>();
    }

    public bool TryGetCraftPack(uint craftPackId, out CraftPack craftPack)
    {
        craftPack = null;
        return _craftPacks != null && _craftPacks.TryGetValue(craftPackId, out craftPack);
    }

    public IReadOnlyCollection<uint> GetUnresolvedCraftPackIds()
    {
        return _unresolvedCraftPackIds ?? (IReadOnlyCollection<uint>)Array.Empty<uint>();
    }

    public IReadOnlyCollection<uint> GetUnresolvedProductPackIds()
    {
        return _unresolvedProductPackIds ?? (IReadOnlyCollection<uint>)Array.Empty<uint>();
    }

    public IReadOnlyCollection<CraftCategoryMismatch> GetCraftCategoryMismatches()
    {
        return _craftCategoryMismatches ?? (IReadOnlyCollection<CraftCategoryMismatch>)Array.Empty<CraftCategoryMismatch>();
    }

    internal void LoadCraftPackMembership(SqliteConnection connection)
    {
        _craftsByPack = [];
        _unresolvedCraftPackIds ??= [];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT craft_pack_id, craft_id FROM craft_pack_crafts ORDER BY craft_pack_id, craft_id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var craftPackId = reader.GetUInt32("craft_pack_id");
            var craftId = reader.GetUInt32("craft_id");
            if (craftPackId == 0)
                throw new InvalidDataException($"craft_pack_crafts craft {craftId} has a zero craft_pack_id.");
            if (!_crafts.TryGetValue(craftId, out var craft))
            {
                Logger.Warn("Skipping craft_pack_crafts pack {0}: missing crafts row {1}.", craftPackId, craftId);
                continue;
            }

            CraftPack pack = null;
            if (_craftPacks != null && !_craftPacks.TryGetValue(craftPackId, out pack))
            {
                _unresolvedCraftPackIds.Add(craftPackId);
            }
            else if (pack != null)
            {
                pack.CraftIds.Add(craftId);
            }

            if (!_craftsByPack.TryGetValue(craftPackId, out var craftIds))
            {
                craftIds = [];
                _craftsByPack.Add(craftPackId, craftIds);
            }
            craftIds.Add(craftId);
            craft.CraftPackIds.Add(craftPackId);
            craft.IsPack = true;
        }

        if (_unresolvedCraftPackIds.Count > 0)
            Logger.Error("craft_pack_crafts references {0} pack ids without craft_packs rows.", _unresolvedCraftPackIds.Count);
    }
}
