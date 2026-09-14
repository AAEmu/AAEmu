using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Item conversions (evenstone extraction, awakening, repackaging, ...).
///
/// The content splits one conversion into a reagent side and a product side, joined by
/// <c>item_conv_rpack_members</c> / <c>item_conv_ppack_members</c>:
///
/// <code>
/// item_conv_reagents / item_conv_reagent_filters -> item_conv_rpacks
///     -> item_conv_rpack_members -> item_convs -> item_conv_ppack_members
///     -> item_conv_ppacks (chance_rate) -> item_conv_products (weight, min, max, item_grade_id)
/// </code>
///
/// <c>item_convs.item_conv_set_id</c> names the <c>item_conv_sets</c> family (disenchant, awakening, ...)
/// whose id the ItemConversion special effect carries in <c>value1</c>.
///
/// Earlier revisions ignored every pack table: reagents were keyed straight off <c>item_conv_rpack_id</c>,
/// a product was picked by matching <c>item_conv_products.item_conv_ppack_id</c> against that same rpack id,
/// weights and pack chances went unused, and <c>ConversionSet</c> was never filled because the map it came
/// from was never populated. The same-id match is not nonsense - the content pairs the two packs by id for
/// most conversions - but it reaches 34179 of the 34900 explicit reagent rows and misses the 148 packs that
/// are only linked through <c>item_conv_rpack_members</c>. Following the members reaches 34898.
/// </summary>
[GameData]
public class ItemConversionGameData : Singleton<ItemConversionGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Chance rates in <c>item_conv_ppacks</c> are per ten-thousand.</summary>
    private const int ChanceRateScale = 10000;

    private Dictionary<uint, List<ItemConversionReagent>> _reagentsByItem = [];
    private List<ItemConversionReagent> _filterReagents = [];
    private Dictionary<uint, ItemConversionProductPack> _productPacks = [];
    private Dictionary<uint, ItemConversionSet> _conversionSets = [];
    private Dictionary<uint, uint> _conversionSetByConversion = [];
    private Dictionary<uint, List<uint>> _reagentPackConversions = [];
    private Dictionary<uint, List<uint>> _conversionProductPacks = [];
    private Dictionary<uint, List<int>> _exceptionCategoriesByPack = [];

    /// <summary>Every family the content defines; the ItemConversion effect validates its value1 against it.</summary>
    public IReadOnlyCollection<uint> ConversionSetIds => _conversionSets.Keys;

    public ItemConversionSet GetConversionSet(uint id) =>
        _conversionSets.GetValueOrDefault(id);

    /// <summary>
    /// Finds the reagent pack an item disenchants/upgrades through. Explicit <c>item_conv_reagents</c> rows
    /// win over the impl/level/grade filters, matching the client's own lookup order.
    /// </summary>
    /// <param name="itemCategoryId">
    /// <c>items.category_id</c>, tested against the filter's exception pack. Filters whose pack excludes
    /// this category are skipped.
    /// </param>
    public ItemConversionReagent GetReagentForItem(byte grade, ItemImplEnum implId, uint itemId, int level, int itemCategoryId = 0)
    {
        if (_reagentsByItem.TryGetValue(itemId, out var explicitReagents))
        {
            foreach (var reagent in explicitReagents)
            {
                if (reagent.MatchesGrade(grade))
                    return reagent;
            }
        }

        foreach (var reagent in _filterReagents)
        {
            if (implId != reagent.ImplId
                || level < reagent.MinLevel || level > reagent.MaxLevel
                || !reagent.MatchesGrade(grade))
                continue;

            if (IsExcludedByExceptionPack(reagent.ExceptionPackId, itemCategoryId))
                continue;

            return reagent;
        }

        return null;
    }

    /// <summary>
    /// Rolls one product for a reagent pack. Returns false only when the conversion has no product rows at
    /// all, which is a data error the caller should surface. A failed chance roll returns true with a null
    /// product so the caller still consumes the reagent.
    /// </summary>
    public bool TryRollProduct(ItemConversionReagent reagent, out ItemConversionRoll roll)
    {
        roll = null;
        if (reagent == null)
            return false;

        var sawProductPack = false;
        foreach (var conversionId in reagent.ConversionIds)
        {
            if (!_conversionProductPacks.TryGetValue(conversionId, out var productPackIds))
                continue;

            foreach (var productPackId in productPackIds)
            {
                if (TryRollFromPack(productPackId, out roll))
                {
                    sawProductPack = true;
                    if (roll.Product != null)
                        return true;
                }
            }
        }

        // 43 of the 5519 reagent packs the content references cannot be reached through the members: 41 have
        // no item_conv_rpack_members row at all ("disuse", the discard-only packs, repackage_socket_skyblue_1T,
        // awakening.6tier_weapon.dagger, two housing blueprints) and two sit in a conversion with no
        // item_conv_ppack_members row. 42 of them still have a product pack whose id equals their own, which
        // is how the retired lookup found them. Keep that as the fallback so the member chain can only ever
        // add reach.
        if (!sawProductPack && TryRollFromPack(reagent.ReagentPackId, out roll))
            return true;

        if (!sawProductPack)
            return false;

        // Every candidate pack rolled its chance and lost: the conversion happened, it just yielded nothing.
        roll = new ItemConversionRoll { Product = null, Count = 0 };
        return true;
    }

    /// <summary>
    /// Rolls against one product pack. False when the pack does not exist or carries no products, so the
    /// caller can tell "nothing to roll" from "rolled and lost" (<see cref="ItemConversionRoll.ChanceFailed"/>).
    /// </summary>
    private bool TryRollFromPack(uint productPackId, out ItemConversionRoll roll)
    {
        roll = null;
        if (!_productPacks.TryGetValue(productPackId, out var pack) || pack.Products.Count == 0)
            return false;

        if (pack.ChanceRate < ChanceRateScale && Random.Shared.Next(ChanceRateScale) >= pack.ChanceRate)
        {
            roll = new ItemConversionRoll { Product = null, Count = 0 };
            return true;
        }

        var product = PickWeighted(pack);
        if (product == null)
            return false;

        roll = new ItemConversionRoll
        {
            Product = product,
            Count = RollOutputCount(product)
        };
        return true;
    }

    /// <summary>
    /// True when the reagent belongs to the conversion family the effect asked for. <c>value1</c> of every
    /// <c>special_effects</c> row of type ItemConversion (49) is an <c>item_conv_sets</c> id - 3 for
    /// evenstone extraction, 7 and 9 for awakening, and so on.
    /// </summary>
    public bool IsValidConversionSet(int conversionSetId, ItemConversionReagent reagent)
    {
        if (conversionSetId <= 0 || reagent == null)
            return false;

        return reagent.ConversionSet == (uint)conversionSetId;
    }

    public void Load(SqliteConnection connection)
    {
        _reagentsByItem = [];
        _filterReagents = [];
        _productPacks = [];
        _conversionSets = [];
        _conversionSetByConversion = [];
        _reagentPackConversions = [];
        _conversionProductPacks = [];
        _exceptionCategoriesByPack = [];

        // Conversion families and the conversions they own.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_sets";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var set = new ItemConversionSet
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name", string.Empty),
                    DialogTitle = reader.GetString("dialog_title", string.Empty),
                    DialogContent = reader.GetString("dialog_content", string.Empty)
                };
                _conversionSets[set.Id] = set;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_convs";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var conversionId = reader.GetUInt32("id");
                // item_conv_set_id is nullable: 43 of 6409 rows carry no family.
                var setId = reader.GetUInt32("item_conv_set_id", 0);
                if (setId == 0)
                    continue;

                _conversionSetByConversion[conversionId] = setId;
                if (_conversionSets.TryGetValue(setId, out var set))
                    set.ConversionIds.Add(conversionId);
            }
        }

        // Reagent packs.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_rpack_members";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var conversionId = reader.GetUInt32("item_conv_id");
                var reagentPackId = reader.GetUInt32("item_conv_rpack_id");
                if (!_reagentPackConversions.TryGetValue(reagentPackId, out var conversions))
                {
                    conversions = [];
                    _reagentPackConversions[reagentPackId] = conversions;
                }

                conversions.Add(conversionId);
            }
        }

        // Reagents: explicit item rows first, then the impl/level/grade filters.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_reagents";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var reagent = new ItemConversionReagent
                {
                    ReagentPackId = reader.GetUInt32("item_conv_rpack_id"),
                    InputItemId = reader.GetUInt32("item_id"),
                    // grade_id is nullable in 10.0.2.13; default to 1 (schema default)
                    MinItemGrade = reader.GetByte("grade_id", 1),
                    MaxItemGrade = reader.GetByte("max_grade_id", 0),
                    IsExplicitItem = true
                };

                if (!_reagentsByItem.TryGetValue(reagent.InputItemId, out var list))
                {
                    list = [];
                    _reagentsByItem[reagent.InputItemId] = list;
                }

                list.Add(reagent);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_reagent_filters";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var reagent = new ItemConversionReagent
                {
                    // item_conv_rpack_id is nullable in 10.0.2.13 (24 NULL rows); default to 0
                    ReagentPackId = reader.GetUInt32("item_conv_rpack_id", 0),
                    ImplId = (ItemImplEnum)reader.GetInt32("item_impl_id"),
                    MinLevel = reader.GetInt32("min_level"),
                    MaxLevel = reader.GetInt32("max_level"),
                    MinItemGrade = reader.GetByte("item_grade_id", 0),
                    MaxItemGrade = reader.GetByte("max_item_grade_id", 0),
                    ExceptionPackId = reader.GetUInt32("item_conv_epack_id", 0),
                    IsExplicitItem = false
                };
                _filterReagents.Add(reagent);
            }
        }

        // Exception packs: item categories a reagent filter must not claim.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_exception_filters";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var packId = reader.GetUInt32("item_conv_epack_id", 0);
                if (packId == 0)
                    continue;

                if (!_exceptionCategoriesByPack.TryGetValue(packId, out var categories))
                {
                    categories = [];
                    _exceptionCategoriesByPack[packId] = categories;
                }

                categories.Add(reader.GetInt32("item_category_id"));
            }
        }

        // Product packs and their products.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_ppacks";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var pack = new ItemConversionProductPack
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name", string.Empty),
                    ChanceRate = reader.GetInt32("chance_rate", ChanceRateScale)
                };
                _productPacks[pack.Id] = pack;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_ppack_members";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var conversionId = reader.GetUInt32("item_conv_id");
                var productPackId = reader.GetUInt32("item_conv_ppack_id");
                if (!_conversionProductPacks.TryGetValue(conversionId, out var packs))
                {
                    packs = [];
                    _conversionProductPacks[conversionId] = packs;
                }

                packs.Add(productPackId);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_conv_products";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var productPackId = reader.GetUInt32("item_conv_ppack_id");
                if (!_productPacks.TryGetValue(productPackId, out var pack))
                {
                    Logger.Warn("item_conv_products row {0} names missing product pack {1}", reader.GetUInt32("id"), productPackId);
                    continue;
                }

                pack.Products.Add(new ItemConversionProduct
                {
                    ProductPackId = productPackId,
                    ChanceRate = pack.ChanceRate,
                    OutputItemId = reader.GetUInt32("item_id"),
                    // weight/min/max are nullable in 10.0.2.13; default to 1 (schema default)
                    Weight = reader.GetInt32("weight", 1),
                    MinOutput = reader.GetInt32("min", 1),
                    MaxOutput = reader.GetInt32("max", 1),
                    GradeId = reader.GetInt32("item_grade_id", -1)
                });
            }
        }

        Logger.Info("Loaded {0} conversion sets, {1} reagent packs, {2} product packs",
            _conversionSets.Count, _reagentPackConversions.Count, _productPacks.Count);
    }

    public void PostLoad()
    {
        // Resolve each reagent pack to its conversion family(ies). A pack feeds one conversion in 5744 of
        // 5866 cases and two in the other 122.
        var unresolved = new HashSet<uint>();
        ResolveReagentSets(_filterReagents, unresolved);
        foreach (var list in _reagentsByItem.Values)
            ResolveReagentSets(list, unresolved);

        var setlessConversions = _reagentPackConversions.Values
            .SelectMany(conversions => conversions)
            .Distinct()
            .Count(conversionId => !_conversionSetByConversion.ContainsKey(conversionId));

        if (unresolved.Count > 0)
        {
            // 48 packs in 10.0.2.13 land here (the origin-land armour sockets, the raid-to-Ipnir exchange,
            // the discontinued mate armours). Their reagent rows carry no family, so a conversion effect
            // cannot be validated against them; the products are still reachable and the ItemConversion
            // effect deliberately allows the cast rather than rejecting on missing data.
            Logger.Warn(
                "Item conversions: {0} of {1} reagent packs reach no item_conv_sets family ({2} conversions carry no family); their conversions cannot be validated against the effect",
                unresolved.Count, _reagentPackConversions.Count, setlessConversions);
        }
    }

    private void ResolveReagentSets(List<ItemConversionReagent> reagents, HashSet<uint> unresolvedPacks)
    {
        foreach (var reagent in reagents)
        {
            if (!_reagentPackConversions.TryGetValue(reagent.ReagentPackId, out var conversions) ||
                conversions.Count == 0)
            {
                unresolvedPacks.Add(reagent.ReagentPackId);
                continue;
            }

            reagent.ConversionIds = [.. conversions];
            foreach (var conversionId in conversions)
            {
                if (_conversionSetByConversion.TryGetValue(conversionId, out var setId))
                {
                    reagent.ConversionSet = setId;
                    break;
                }
            }

            if (reagent.ConversionSet == 0)
                unresolvedPacks.Add(reagent.ReagentPackId);
        }
    }

    private bool IsExcludedByExceptionPack(uint exceptionPackId, int itemCategoryId)
    {
        if (exceptionPackId == 0 || itemCategoryId == 0)
            return false;

        return _exceptionCategoriesByPack.TryGetValue(exceptionPackId, out var categories) &&
               categories.Contains(itemCategoryId);
    }

    private static ItemConversionProduct PickWeighted(ItemConversionProductPack pack)
    {
        var totalWeight = pack.TotalWeight;
        if (totalWeight <= 0)
            return pack.Products[0];

        var roll = Random.Shared.Next(totalWeight);
        foreach (var product in pack.Products)
        {
            var weight = Math.Max(0, product.Weight);
            if (roll < weight)
                return product;
            roll -= weight;
        }

        return pack.Products[^1];
    }

    private static int RollOutputCount(ItemConversionProduct product)
    {
        // 60 rows carry min = max = 0: the conversion consumes the reagent and yields nothing.
        if (product.MaxOutput <= product.MinOutput)
            return product.MinOutput;

        return Random.Shared.Next(product.MinOutput, product.MaxOutput + 1);
    }
}
