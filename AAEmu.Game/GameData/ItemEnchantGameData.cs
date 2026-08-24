using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Static data behind the three item-enchant systems that the 10.0.2.13 client exposes next to
/// regrade: tempering (<c>enchant_scale_ratios</c>), awakening (<c>item_change_mapping*</c>) and the
/// random-attribute pools synthesis draws from (<c>item_rnd_attr_*</c>).
/// </summary>
[GameData]
public class ItemEnchantGameData : Singleton<ItemEnchantGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<byte, EnchantScaleRatio> _enchantScaleRatios;
    private Dictionary<uint, ItemChangeMappingGroup> _changeMappingGroups;
    private Dictionary<uint, List<ItemChangeMapping>> _changeMappingsBySourceItem;
    private Dictionary<uint, ItemRndAttrCategory> _rndAttrCategories;
    private Dictionary<uint, uint> _evolvingMaterialCategories;
    private Dictionary<uint, List<ItemRndAttrUnitModifierRange>> _rndAttrModifiers;
    private Dictionary<(uint CategoryId, byte GradeId), ItemRndAttrCategoryProperty> _rndAttrProperties;
    private Dictionary<uint, List<ItemRndAttrUnitModifierGroupSet>> _rndAttrGroupSets;
    private Dictionary<uint, List<ItemRndAttrUnitModifierGroup>> _rndAttrGroups;
    private Dictionary<(uint CategoryId, byte Level), ItemRndAttrCategoryElement> _rndAttrElements;
    private HashSet<uint> _capScaleForbids;
    private Dictionary<uint, HashSet<uint>> _rndAttrGroupRelations;
    private Dictionary<uint, byte> _ladderTops;
    private Dictionary<uint, ItemEnchantRatioGroup> _enchantRatioGroups;
    private Dictionary<(uint GroupId, int Grade), ItemEnchantRatio> _enchantRatios;
    private Dictionary<uint, uint> _enchantRatioGroupByItem;
    private Dictionary<uint, uint> _enchantRatioGroupByImpl;
    private uint _defaultEnchantRatioGroupId;

    /// <summary>
    /// The regrade odds that apply to one item at its current grade, or null when the grade has no
    /// row - which is what the top of the ladder looks like.
    /// </summary>
    /// <param name="itemTemplateId">Template id, matched against the custom group lists.</param>
    /// <param name="implId">The template's <c>impl_id</c>, matched against the item_impl groups.</param>
    /// <param name="grade">Current <c>item_grades.id</c> of the item.</param>
    public ItemEnchantRatio GetGradeEnchantRatio(uint itemTemplateId, ItemImplEnum implId, byte grade)
    {
        var groupId = GetGradeEnchantRatioGroupId(itemTemplateId, implId);
        return _enchantRatios.GetValueOrDefault((groupId, grade));
    }

    /// <summary>
    /// Which <c>item_enchant_ratio_groups</c> row governs an item.
    /// </summary>
    /// <remarks>
    /// The kinds are asked most specific first, which is the only order that can work: an explicit
    /// list names individual items, an item_impl group claims a whole item class, and default takes
    /// whatever is left. Shipped data has 2121 items on custom lists and three impl groups (slave
    /// equipment, mount armor, summoned slaves); ordinary weapons, armor and accessories land on
    /// the default group.
    /// </remarks>
    public uint GetGradeEnchantRatioGroupId(uint itemTemplateId, ItemImplEnum implId)
    {
        if (_enchantRatioGroupByItem.TryGetValue(itemTemplateId, out var customGroup))
            return customGroup;

        if (_enchantRatioGroupByImpl.TryGetValue((uint)implId, out var implGroup))
            return implGroup;

        return _defaultEnchantRatioGroupId;
    }

    /// <summary>
    /// The tempering step that applies while an item sits at <paramref name="currentScale"/>.
    /// Returns null past the end of the ladder.
    /// </summary>
    public EnchantScaleRatio GetEnchantScaleRatio(byte currentScale)
    {
        return _enchantScaleRatios.GetValueOrDefault(currentScale);
    }

    /// <summary>Display name for a scale, e.g. "+7". Row 0 is called "none" in the data.</summary>
    public string GetEnchantScaleName(byte scale)
    {
        var ratio = GetEnchantScaleRatio(scale);
        return ratio == null || scale == 0 ? "+0" : ratio.Name;
    }

    /// <summary>
    /// Whether an item is barred from tempering outright. Most of <c>item_cap_scale_forbids</c>
    /// names items that carry no ceiling anyway, but a couple do, so the list is its own rule rather
    /// than a restatement of items.max_enchant_scale_id.
    /// </summary>
    public bool IsCapScaleForbidden(uint itemId)
    {
        return _capScaleForbids.Contains(itemId);
    }

    /// <summary>Scale value carried by a step, used to derive the item's stat multiplier.</summary>
    public short GetEnchantScaleValue(byte scale)
    {
        return GetEnchantScaleRatio(scale)?.Scale ?? 0;
    }

    public ItemChangeMappingGroup GetChangeMappingGroup(uint groupId)
    {
        return _changeMappingGroups.GetValueOrDefault(groupId);
    }

    /// <summary>
    /// Every recipe in <paramref name="groupId"/> that accepts <paramref name="itemId"/> at
    /// <paramref name="grade"/>. More than one is normal - that is what the awaken tab's radio
    /// buttons pick between.
    /// </summary>
    public List<ItemChangeMapping> GetChangeMappings(uint groupId, uint itemId, byte grade)
    {
        if (!_changeMappingsBySourceItem.TryGetValue(itemId, out var candidates))
            return [];

        var result = new List<ItemChangeMapping>();
        foreach (var mapping in candidates)
        {
            if (mapping.MappingGroupId != groupId)
                continue;
            if (mapping.SourceGradeId >= 0 && mapping.SourceGradeId != grade)
                continue;
            result.Add(mapping);
        }

        return result;
    }

    public ItemRndAttrCategory GetRndAttrCategory(uint categoryId)
    {
        return _rndAttrCategories.GetValueOrDefault(categoryId);
    }

    /// <summary>
    /// The random-attribute category a synthesis material feeds, or 0 when the item is not a
    /// synthesis material at all.
    /// </summary>
    public uint GetEvolvingMaterialCategory(uint itemId)
    {
        return _evolvingMaterialCategories.GetValueOrDefault(itemId, 0u);
    }

    /// <summary>Modifier ranges available to a random-attribute group at a given item grade.</summary>
    public List<ItemRndAttrUnitModifierRange> GetRndAttrModifiers(uint groupId, byte grade)
    {
        if (!_rndAttrModifiers.TryGetValue(groupId, out var all))
            return [];

        var exact = all.FindAll(m => m.GradeId == grade);
        return exact.Count > 0 ? exact : all;
    }

    /// <summary>
    /// Whether a material may be fed to a target, which is a question about their category
    /// <em>groups</em> rather than their categories: item_rnd_attr_category_relations lists, per
    /// target group, the material groups it accepts. Main-quest gear (group 11) taking main-quest
    /// infusions (group 12) is one such pair, and the two carry different category ids, so comparing
    /// those directly rejects every legitimate feed.
    /// </summary>
    public bool AcceptsMaterial(uint targetCategoryId, uint materialCategoryId)
    {
        var targetGroup = GetRndAttrCategory(targetCategoryId)?.GroupId ?? 0;
        var materialGroup = GetRndAttrCategory(materialCategoryId)?.GroupId ?? 0;

        if (targetGroup == 0 || materialGroup == 0)
            return false;

        if (targetGroup == materialGroup)
            return true;

        return _rndAttrGroupRelations.TryGetValue(targetGroup, out var accepted) &&
               accepted.Contains(materialGroup);
    }

    /// <summary>What a pool costs and grants at one synthesis grade.</summary>
    public ItemRndAttrCategoryProperty GetRndAttrProperty(uint categoryId, byte grade)
    {
        return _rndAttrProperties.GetValueOrDefault((categoryId, grade));
    }

    /// <summary>
    /// The experience an item at <paramref name="grade"/> needs to reach the next one.
    /// </summary>
    /// <remarks>
    /// What an item stores is the progress inside its current grade, not a running total: the client
    /// prints it straight against this number ("Current XP 520/230") and calls the ratio the item's
    /// percentage. A step subtracts this and carries the remainder into the next grade, which is why
    /// a piece reads 0% right after one. A value of 0 is a rung that costs nothing rather than a
    /// wall - the shipped ladders use it to step over Crude, a grade the client does not list.
    /// </remarks>
    public uint GetGradeExp(uint categoryId, byte grade)
    {
        return GetRndAttrProperty(categoryId, grade)?.GradeExp ?? 0;
    }

    /// <summary>
    /// The lowest grade of a pool that grants a synthesis effect at all.
    /// </summary>
    /// <remarks>
    /// This is where an awakened item enters its new tier, and the shipped pools line up with the
    /// awakening window exactly: the main-quest one-handed chain goes 1T (no rung grants anything),
    /// 2T grade 2, 3T grade 3, 4T grade 4 - which is the Arcane to Grand, Heroic to Rare and Unique
    /// to Arcane the previews show. The bottom rungs of a pool carry a
    /// <c>max_unit_modifier_num</c> of 0 and exist only as steps, so the first rung that grants
    /// something is the tier's real starting grade.
    /// </remarks>
    /// <summary>
    /// The highest grade a pool's own ladder can actually reach.
    /// </summary>
    /// <remarks>
    /// Not <c>max_evolving_grade</c>. That column says Celestial on 360 categories whose
    /// <c>grade_exp</c> rows go on charging real prices well past it - Divine, Epic, Legendary,
    /// Eternal - which dead-ends gear that is meant to keep growing, since the scrolls that awaken it
    /// require those grades. The ladder itself is the honest answer: the last rung that costs
    /// something, plus the grade it leads to. The client gates its own window on the shipped column,
    /// so lifting it there needs a patch to the client database; this only stops the server from
    /// enforcing a ceiling the data does not mean.
    /// </remarks>
    public byte GetLadderTop(uint categoryId)
    {
        return _ladderTops.TryGetValue(categoryId, out var top) ? top : (byte)0;
    }

    /// <summary>
    /// Everything a piece has ever banked in this pool: the rungs it has already climbed plus the
    /// progress it holds inside the current one.
    /// </summary>
    /// <remarks>
    /// An item stores only the progress inside its grade, because that is what the tooltip prints.
    /// The running total is never stored and never needs to be - it is exactly this sum, and it is
    /// what an awakening carries into the next tier.
    /// </remarks>
    public uint GetCumulativeExp(uint categoryId, byte grade, uint sectionExp)
    {
        var total = sectionExp;
        for (byte g = 0; g < grade; g++)
            total += GetGradeExp(categoryId, g);

        return total;
    }

    /// <summary>
    /// Where a running total lands on a pool's ladder: the grade it reaches and the progress left
    /// inside it.
    /// </summary>
    /// <remarks>
    /// This is what places an awakened piece in its new tier. The ladders are built so the totals
    /// line up exactly - a Brilliant Jerkin maxed at Unique has banked 1607, and 1607 is precisely
    /// what the Hiram Jerkin's ladder charges to reach Arcane. Across the shipped mappings 7437 of
    /// 7658 land on an exact rung this way.
    /// </remarks>
    public (byte Grade, uint SectionExp) PlaceCumulativeExp(uint categoryId, uint total)
    {
        var top = GetLadderTop(categoryId);
        byte grade = 0;
        var left = total;
        while (grade < top && grade < byte.MaxValue)
        {
            var cost = GetGradeExp(categoryId, grade);
            if (left < cost)
                break;

            left -= cost;
            grade++;
        }

        return (grade, left);
    }

    /// <summary>
    /// What a pool charges to climb from where a piece stands to the top of its ladder. Anything fed
    /// beyond this is the overflow the synthesis window reports, and buys nothing.
    /// </summary>
    public uint GetExpToMaxGrade(uint categoryId, byte grade, uint sectionExp)
    {
        var top = GetLadderTop(categoryId);
        if (grade >= top)
            return 0;

        var remaining = 0u;
        for (var g = grade; g < top && g < byte.MaxValue; g++)
            remaining += GetGradeExp(categoryId, (byte)g);

        return remaining > sectionExp ? remaining - sectionExp : 0;
    }

    /// <returns>
    /// False when the pool grants no effects at any grade. Plenty of pools are like that - the
    /// main-quest 1T weapons among them - and reading "no rung" as "rung 0" would drop an awakened
    /// piece to Basic instead of leaving its grade alone.
    /// </returns>
    public bool TryGetFirstEffectGrade(uint categoryId, out byte firstGrade)
    {
        firstGrade = 0;
        var category = GetRndAttrCategory(categoryId);
        if (category == null)
            return false;

        for (byte grade = 0; grade <= category.MaxEvolvingGrade && grade < byte.MaxValue; grade++)
        {
            var property = GetRndAttrProperty(categoryId, grade);
            if (property is not { MaxUnitModifierNum: > 0 })
                continue;

            firstGrade = grade;
            return true;
        }

        return false;
    }

    /// <summary>How many rolled attributes a pool allows at a grade, capped by what an item can store.</summary>
    public int GetRndAttrCap(uint categoryId, byte grade)
    {
        var property = GetRndAttrProperty(categoryId, grade);
        return Math.Min((int)(property?.MaxUnitModifierNum ?? 0), EquipItem.RndAttrSlots);
    }

    /// <summary>One rung of a pool's element ladder, or null past its top.</summary>
    public ItemRndAttrCategoryElement GetElementStep(uint categoryId, byte level)
    {
        return _rndAttrElements.GetValueOrDefault((categoryId, level));
    }

    /// <summary>
    /// Draws the effect groups a pool grants, the way the "Available Effects" window lays them out:
    /// every bundle contributes, each up to its own <c>pick_num</c> ("Randomly select up to N"), and
    /// no attribute appears twice ("Can't result in two of the same effect").
    /// </summary>
    /// <remarks>
    /// The bundles are not alternatives to pick between. A category's sets sum their <c>pick_num</c>
    /// to that category's <c>max_unit_modifier_num</c>, which is what makes a pool with a 1-pick and
    /// a 2-pick bundle grant three effects. <paramref name="exclude"/> carries the attributes an item
    /// already holds, so topping a piece up does not hand it the same line twice.
    /// </remarks>
    public List<uint> RollRndAttrGroups(uint categoryId, byte grade, IReadOnlyCollection<uint> held = null)
    {
        var result = new List<uint>();
        if (!_rndAttrGroupSets.TryGetValue(categoryId, out var sets) || sets.Count == 0)
            return result;

        held ??= [];
        var taken = new HashSet<short>(GetRndAttrAttributes(held));

        foreach (var set in sets)
        {
            if (!_rndAttrGroups.TryGetValue(set.Id, out var groups) || groups.Count == 0)
                continue;

            // A bundle's allowance is per item, not per draw: what the piece already holds from this
            // bundle counts against it. Topping a piece up therefore has to ask each bundle what it
            // has left, or the first bundle hands out its one main stat again on every pass.
            var picks = Math.Max(1, set.PickNum) - groups.Count(g => held.Contains(g.Id));
            if (picks <= 0)
                continue;

            // Fixed entries are granted rather than rolled for, and they use up the bundle's picks.
            var candidates = new List<ItemRndAttrUnitModifierGroup>();
            foreach (var group in groups)
            {
                if (!group.FixedAttr)
                    candidates.Add(group);
                else if (taken.Add(group.UnitAttributeId))
                {
                    result.Add(group.Id);
                    picks--;
                }
            }

            while (picks > 0 && candidates.Count > 0)
            {
                var group = PickWeighted(candidates, g => g.Weight);
                if (group == null)
                    break;

                candidates.Remove(group);
                if (!taken.Add(group.UnitAttributeId))
                    continue; // already on the piece from another bundle - try the next candidate

                result.Add(group.Id);
                picks--;
            }
        }

        return result;
    }

    /// <summary>
    /// Draws a single effect group, for the swap actions that replace one line rather than granting
    /// a grade's worth.
    /// </summary>
    /// <remarks>
    /// A swap stays inside the bundle the replaced line came from. Each bundle owns one of the
    /// item's lines and offers its own choices, so replacing the second line can only ever draw
    /// what the second bundle carries - drawing across the whole pool answered a swap of one line
    /// with an effect the other bundle owns. Pass 0 to draw from the pool at large.
    /// </remarks>
    public uint RollRndAttrGroup(uint categoryId, byte grade, ICollection<short> exclude = null,
        uint sameBundleAs = 0)
    {
        if (!_rndAttrGroupSets.TryGetValue(categoryId, out var sets) || sets.Count == 0)
            return 0;

        var taken = new HashSet<short>(exclude ?? []);
        var candidates = new List<ItemRndAttrUnitModifierGroup>();
        foreach (var set in sets)
        {
            if (!_rndAttrGroups.TryGetValue(set.Id, out var groups))
                continue;
            if (sameBundleAs != 0 && !groups.Exists(g => g.Id == sameBundleAs))
                continue;
            foreach (var group in groups)
            {
                if (!taken.Contains(group.UnitAttributeId))
                    candidates.Add(group);
            }
        }

        return PickWeighted(candidates, g => g.Weight)?.Id ?? 0;
    }

    /// <summary>
    /// Whether two effect groups are offered by the same bundle of a pool.
    /// </summary>
    public bool IsGroupInSameBundle(uint categoryId, uint groupId, uint otherGroupId)
    {
        if (groupId == otherGroupId)
            return true;
        if (!_rndAttrGroupSets.TryGetValue(categoryId, out var sets))
            return false;

        foreach (var set in sets)
        {
            if (!_rndAttrGroups.TryGetValue(set.Id, out var groups))
                continue;
            if (groups.Exists(g => g.Id == groupId))
                return groups.Exists(g => g.Id == otherGroupId);
        }

        return false;
    }

    /// <summary>
    /// Re-homes an item's effect groups into another pool, matching them by the attribute they carry.
    /// </summary>
    /// <remarks>
    /// An awakened piece keeps its effects, and the awakening window previews them re-valued for the
    /// grade it lands on - "Spirit 27 to 44". What survives is therefore the attribute, not the group:
    /// the group id belongs to the pool it was drawn from, and its magnitude is only defined there.
    /// Carrying the old id across leaves an effect whose value cannot be looked up in the new pool and,
    /// worse, one that no bundle of that pool recognises as its own - so the bundle that already
    /// supplied it hands out a second of the same kind, which is how a piece ends up wearing two main
    /// stats. An attribute the new pool does not offer at all is dropped.
    /// </remarks>
    public List<uint> TranslateGroupsToCategory(IEnumerable<uint> groupIds, uint categoryId)
    {
        var translated = new List<uint>();
        if (!_rndAttrGroupSets.TryGetValue(categoryId, out var sets))
            return translated;

        foreach (var groupId in groupIds)
        {
            var source = FindGroup(groupId);
            if (source == null)
                continue;

            foreach (var set in sets)
            {
                if (!_rndAttrGroups.TryGetValue(set.Id, out var groups))
                    continue;

                var match = groups.Find(g => g.UnitAttributeId == source.UnitAttributeId &&
                                             g.UnitModifierTypeId == source.UnitModifierTypeId);
                if (match == null || translated.Contains(match.Id))
                    continue;

                translated.Add(match.Id);
                break;
            }
        }

        return translated;
    }

    /// <summary>Whether an effect group belongs to one of a pool's bundles.</summary>
    public bool IsGroupInCategory(uint groupId, uint categoryId)
    {
        if (!_rndAttrGroupSets.TryGetValue(categoryId, out var sets))
            return false;

        foreach (var set in sets)
        {
            if (_rndAttrGroups.TryGetValue(set.Id, out var groups) && groups.Exists(g => g.Id == groupId))
                return true;
        }

        return false;
    }

    /// <summary>The unit attributes a run of effect groups covers, for keeping a roll duplicate-free.</summary>
    public List<short> GetRndAttrAttributes(IEnumerable<uint> groupIds)
    {
        var attributes = new List<short>();
        foreach (var groupId in groupIds)
        {
            var group = FindGroup(groupId);
            if (group != null)
                attributes.Add(group.UnitAttributeId);
        }

        return attributes;
    }

    /// <summary>
    /// What an effect group is worth on an item, given the grade it sits at and how far it has come
    /// toward the next one.
    /// </summary>
    /// <remarks>
    /// The magnitude is neither rolled nor stored: it is read off the group's range for the grade and
    /// slid across that range by the item's synthesis progress. That is why an item keeps only the
    /// group id, why the window offers a span ("Strength 34~39") instead of a number, and why a piece
    /// halfway to its next grade already carries about half of what that grade adds. Rolling instead
    /// would have the same item report different stats every time its gear was re-totalled.
    /// </remarks>
    public ItemRndAttrUnitModifier GetRndAttrModifier(uint groupId, byte grade, uint exp = 0)
    {
        var group = FindGroup(groupId);
        if (group == null)
            return null;

        var ranges = GetRndAttrModifiers(groupId, grade);
        var value = 0L;
        if (ranges.Count > 0)
        {
            var range = ranges[0];
            value = range.Min;

            var span = range.Max - range.Min;
            // Not "span > 0": a reducing line runs from -18 to -25, so its span is negative and the
            // guard skipped it, pinning every such effect to the weak end of its own range.
            if (span != 0 && exp > 0)
            {
                // The section length is the grade's own grade_exp; a rung that costs nothing has no
                // progress to make and stays at its floor.
                // exp is already the progress inside the current grade, so it slides across the range
                // directly.
                var section = GetGradeExp(FindCategoryForGroupSet(group.GroupSetId), grade);
                if (section > 0)
                    value += (long)span * Math.Min(exp, section) / section;
            }
        }

        return new ItemRndAttrUnitModifier
        {
            Attribute = group.UnitAttributeId,
            ModifierType = group.UnitModifierTypeId,
            Value = (int)value
        };
    }

    private ItemRndAttrUnitModifierGroup FindGroup(uint groupId)
    {
        foreach (var groups in _rndAttrGroups.Values)
        {
            var group = groups.Find(g => g.Id == groupId);
            if (group != null)
                return group;
        }

        return null;
    }

    private uint FindCategoryForGroupSet(uint groupSetId)
    {
        foreach (var (categoryId, sets) in _rndAttrGroupSets)
        {
            if (sets.Exists(s => s.Id == groupSetId))
                return categoryId;
        }

        return 0;
    }

    /// <summary>
    /// Weighted pick. Rows with a weight of 0 are still reachable - the data uses 0 for "rare, but
    /// not impossible" often enough that dropping them would silently shrink the pool - so every
    /// entry counts for at least one.
    /// </summary>
    private static T PickWeighted<T>(List<T> entries, Func<T, int> weightOf)
    {
        var total = 0;
        foreach (var entry in entries)
            total += Math.Max(1, weightOf(entry));

        var roll = Random.Shared.Next(total);
        foreach (var entry in entries)
        {
            roll -= Math.Max(1, weightOf(entry));
            if (roll < 0)
                return entry;
        }

        return entries.Count > 0 ? entries[^1] : default;
    }

    /// <summary>Nothing to resolve after the fact - every lookup here is self-contained.</summary>
    public void PostLoad()
    {
    }

    public void Load(SqliteConnection connection)
    {
        _enchantScaleRatios = [];
        _changeMappingGroups = [];
        _changeMappingsBySourceItem = [];
        _rndAttrCategories = [];
        _evolvingMaterialCategories = [];
        _rndAttrModifiers = [];
        _rndAttrProperties = [];
        _rndAttrGroupSets = [];
        _rndAttrGroups = [];
        _rndAttrElements = [];
        _capScaleForbids = [];
        _rndAttrGroupRelations = [];
        _ladderTops = [];
        _enchantRatioGroups = [];
        _enchantRatios = [];
        _enchantRatioGroupByItem = [];
        _enchantRatioGroupByImpl = [];
        _defaultEnchantRatioGroupId = 0;

        LoadEnchantScaleRatios(connection);
        LoadEnchantRatios(connection);
        LoadChangeMappings(connection);
        LoadRndAttributes(connection);

        Logger.Info(
            "Item enchant data: {0} temper steps, {1} awakening groups over {2} source items, {3} attribute categories",
            _enchantScaleRatios.Count, _changeMappingGroups.Count, _changeMappingsBySourceItem.Count,
            _rndAttrCategories.Count);
        Logger.Info(
            "Regrade ratios: {0} rows in {1} groups, {2} items listed explicitly, default group {3}",
            _enchantRatios.Count, _enchantRatioGroups.Count, _enchantRatioGroupByItem.Count,
            _defaultEnchantRatioGroupId);
    }

    /// <summary>
    /// Loads the three tables regrade odds live in since 10.0.2.13:
    /// <c>item_enchant_ratio_groups</c> (who a set applies to), <c>item_enchant_ratio_items</c> (the
    /// explicit membership lists) and <c>item_enchant_ratios</c> (the odds themselves).
    /// </summary>
    /// <remarks>
    /// Up to 10.0.2.9 these were <c>grade_enchant_*</c> columns on <c>item_grades</c>, one global
    /// set for the whole server. Reading them from there now yields nothing at all, which left every
    /// chance at zero and so made every attempt report Fail.
    /// </remarks>
    private void LoadEnchantRatios(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratio_groups";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new ItemEnchantRatioGroup
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name", string.Empty),
                    Kind = (ItemEnchantRatioKind)reader.GetInt32("item_enchant_ratio_kind_id", 0),
                    ItemImplId = reader.GetUInt32("item_impl_id", 0)
                };

                _enchantRatioGroups[group.Id] = group;

                switch (group.Kind)
                {
                    case ItemEnchantRatioKind.ItemImpl when group.ItemImplId != 0:
                        _enchantRatioGroupByImpl.TryAdd(group.ItemImplId, group.Id);
                        break;
                    // Only one default ships. Should a second ever appear, the lower id wins so the
                    // choice is at least stable across restarts.
                    case ItemEnchantRatioKind.Default
                        when _defaultEnchantRatioGroupId == 0 || group.Id < _defaultEnchantRatioGroupId:
                        _defaultEnchantRatioGroupId = group.Id;
                        break;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratio_items";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id", 0);
                var groupId = reader.GetUInt32("item_enchant_ratio_group_id", 0);
                if (itemId == 0 || groupId == 0)
                    continue;

                // An item on two lists would make its odds depend on row order; say so rather than
                // letting whichever row came last decide.
                if (!_enchantRatioGroupByItem.TryAdd(itemId, groupId))
                {
                    Logger.Warn("Item {0} is listed in enchant ratio groups {1} and {2}; keeping {1}",
                        itemId, _enchantRatioGroupByItem[itemId], groupId);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_enchant_ratios";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var ratio = new ItemEnchantRatio
                {
                    Id = reader.GetUInt32("id"),
                    GroupId = reader.GetUInt32("item_enchant_ratio_group_id", 0),
                    Grade = reader.GetInt32("grade", -1),
                    SuccessRatio = reader.GetInt32("grade_enchant_success_ratio", 0),
                    GreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio", 0),
                    BreakRatio = reader.GetInt32("grade_enchant_break_ratio", 0),
                    DisableRatio = reader.GetInt32("grade_enchant_disable_ratio", 0),
                    DowngradeRatio = reader.GetInt32("grade_enchant_downgrade_ratio", 0),
                    DowngradeMin = reader.GetInt32("grade_enchant_downgrade_min", -1),
                    DowngradeMax = reader.GetInt32("grade_enchant_downgrade_max", -1),
                    Cost = reader.GetInt32("grade_enchant_cost", 0),
                    CurrencyId = reader.GetUInt32("currency_id", 0)
                };

                if (ratio.Grade < 0)
                    continue;

                _enchantRatios[(ratio.GroupId, ratio.Grade)] = ratio;
            }
        }

        if (_defaultEnchantRatioGroupId == 0)
            Logger.Warn("item_enchant_ratio_groups has no default group; items on no list cannot be regraded");
    }

    private void LoadEnchantScaleRatios(SqliteConnection connection)
    {
        using (var forbidCommand = connection.CreateCommand())
        {
            forbidCommand.CommandText = "SELECT * FROM item_cap_scale_forbids";
            forbidCommand.Prepare();

            using var forbidSqliteReader = forbidCommand.ExecuteReader();
            using var forbidReader = new SQLiteWrapperReader(forbidSqliteReader);
            while (forbidReader.Read())
            {
                var itemId = forbidReader.GetUInt32("item_id", 0);
                if (itemId != 0)
                    _capScaleForbids.Add(itemId);
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM enchant_scale_ratios ORDER BY id";
        command.Prepare();

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var ratio = new EnchantScaleRatio
            {
                Id = reader.GetByte("id"),
                Name = reader.IsDBNull("name") ? string.Empty : reader.GetString("name"),
                Scale = (short)reader.GetInt32("scale", 0),
                SuccessRatio = reader.GetInt32("success_ratio", 0),
                // The column really is spelled "grate_" in the client data.
                GreatSuccessRatio = reader.GetInt32("grate_success_ratio", 0),
                BreakRatio = reader.GetInt32("break_ratio", 0),
                DisableRatio = reader.GetInt32("disable_ratio", 0),
                DownRatio = reader.GetInt32("down_ratio", 0),
                DownMax = reader.GetByte("down_max", 0),
                Cost = reader.GetInt32("cost", 0),
                CurrencyId = reader.GetUInt32("currency_id", 0)
            };

            // "+30" is listed twice at the top of the ladder; the first row wins.
            _enchantScaleRatios.TryAdd(ratio.Id, ratio);
        }
    }

    private void LoadChangeMappings(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_change_mapping_groups";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new ItemChangeMappingGroup
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.IsDBNull("name") ? string.Empty : reader.GetString("name"),
                    Success = reader.GetInt32("success", 0),
                    Disable = reader.GetInt32("disable", 0),
                    FailBonus = reader.GetInt32("fail_bonus", 0),
                    Selectable = reader.GetBoolean("selectable", true),
                    EvolvingExpInherit = reader.GetBoolean("evolving_exp_inherit", true)
                };
                _changeMappingGroups.TryAdd(group.Id, group);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_change_mappings";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var mapping = new ItemChangeMapping
                {
                    Id = reader.GetUInt32("id"),
                    MappingGroupId = reader.GetUInt32("mapping_group_id"),
                    SourceItemId = reader.GetUInt32("source_item_id"),
                    TargetItemId = reader.GetUInt32("target_item_id"),
                    SourceGradeId = reader.GetInt32("source_grade_id", -1),
                    TargetGradeId = reader.GetInt32("target_grade_id", -1)
                };

                if (mapping.SourceItemId == 0 || mapping.TargetItemId == 0)
                    continue;

                if (!_changeMappingsBySourceItem.TryGetValue(mapping.SourceItemId, out var list))
                {
                    list = [];
                    _changeMappingsBySourceItem.Add(mapping.SourceItemId, list);
                }
                list.Add(mapping);

                if (_changeMappingGroups.TryGetValue(mapping.MappingGroupId, out var group))
                    group.Mappings.Add(mapping);
            }
        }
    }

    private void LoadRndAttributes(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_categories";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var category = new ItemRndAttrCategory
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.IsDBNull("name") ? string.Empty : reader.GetString("name"),
                    MaxEvolvingGrade = reader.GetInt32("max_evolving_grade", 0),
                    ReRollItemSetId = reader.GetUInt32("re_roll_item_set_id", 0),
                    MaterialGradeLimit = reader.GetInt32("material_grade_limit", 255),
                    CurrencyId = reader.GetUInt32("currency_id", 0),
                    GroupId = reader.GetUInt32("item_rnd_attr_category_group_id", 0)
                };
                _rndAttrCategories.TryAdd(category.Id, category);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_evolving_materials";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var itemId = reader.GetUInt32("item_id", 0);
                if (itemId == 0)
                    continue;
                _evolvingMaterialCategories[itemId] = reader.GetUInt32("item_rnd_attr_category_id", 0);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_unit_modifiers";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var range = new ItemRndAttrUnitModifierRange
                {
                    Id = reader.GetUInt32("id"),
                    GroupId = reader.GetUInt32("group_id", 0),
                    GradeId = reader.GetByte("grade_id", 0),
                    Min = reader.GetInt32("min", 0),
                    Max = reader.GetInt32("max", 0)
                };

                if (!_rndAttrModifiers.TryGetValue(range.GroupId, out var list))
                {
                    list = [];
                    _rndAttrModifiers.Add(range.GroupId, list);
                }
                list.Add(range);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_category_properties";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var property = new ItemRndAttrCategoryProperty
                {
                    CategoryId = reader.GetUInt32("item_rnd_attr_category_id", 0),
                    GradeId = reader.GetByte("grade_id", 0),
                    ReqExp = reader.GetUInt32("req_exp", 0),
                    GainExp = reader.GetUInt32("gain_exp", 0),
                    MaxUnitModifierNum = reader.GetByte("max_unit_modifier_num", 0),
                    BonusExpChance = reader.GetInt32("bonus_exp_chance", 0),
                    BonusExpMin = reader.GetInt32("bonus_exp_min", 100),
                    BonusExpMax = reader.GetInt32("bonus_exp_max", 100),
                    GradeExp = reader.GetUInt32("grade_exp", 0),
                    GoldMul = reader.GetInt32("gold_mul", 0),
                    MaxElementLevel = reader.GetByte("max_element_level", 0)
                };
                _rndAttrProperties[(property.CategoryId, property.GradeId)] = property;

                // The ladder's real top: a rung is priced by the grade it leads into, so the highest
                // grade that carries a price is the highest grade this pool offers. Reading it as
                // "one above the last priced rung" let every pool run a grade too far - a Radiant
                // piece reached Epic where its ladder stops at Divine.
                if (property.GradeExp > 0)
                {
                    var reached = property.GradeId;
                    if (!_ladderTops.TryGetValue(property.CategoryId, out var top) || reached > top)
                        _ladderTops[property.CategoryId] = reached;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_unit_modifier_group_sets";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var set = new ItemRndAttrUnitModifierGroupSet
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.IsDBNull("name") ? string.Empty : reader.GetString("name"),
                    CategoryId = reader.GetUInt32("item_rnd_attr_category_id", 0),
                    Weight = reader.GetInt32("weight", 10),
                    PickNum = reader.GetInt32("pick_num", 1),
                    InheritPriorityId = reader.GetUInt32("inherit_priority_id", 0)
                };

                if (!_rndAttrGroupSets.TryGetValue(set.CategoryId, out var list))
                {
                    list = [];
                    _rndAttrGroupSets.Add(set.CategoryId, list);
                }
                list.Add(set);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_unit_modifier_groups";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var group = new ItemRndAttrUnitModifierGroup
                {
                    Id = reader.GetUInt32("id"),
                    Weight = reader.GetInt32("weight", 0),
                    UnitAttributeId = (short)reader.GetInt32("unit_attribute_id", 0),
                    UnitModifierTypeId = reader.GetByte("unit_modifier_type_id", 0),
                    GroupSetId = reader.GetUInt32("item_rnd_attr_unit_modifier_group_set_id", 0),
                    FixedAttr = reader.GetBoolean("fixed_attr", true)
                };

                if (!_rndAttrGroups.TryGetValue(group.GroupSetId, out var list))
                {
                    list = [];
                    _rndAttrGroups.Add(group.GroupSetId, list);
                }
                list.Add(group);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_category_relations";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var targetGroup = reader.GetUInt32("item_rnd_attr_category_group_id", 0);
                var materialGroup = reader.GetUInt32("material_id", 0);
                if (targetGroup == 0 || materialGroup == 0)
                    continue;

                if (!_rndAttrGroupRelations.TryGetValue(targetGroup, out var accepted))
                {
                    accepted = [];
                    _rndAttrGroupRelations.Add(targetGroup, accepted);
                }
                accepted.Add(materialGroup);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_rnd_attr_category_elements";
            command.Prepare();

            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var element = new ItemRndAttrCategoryElement
                {
                    CategoryId = reader.GetUInt32("item_rnd_attr_category_id", 0),
                    Level = reader.GetByte("level", 0),
                    ReqExp = reader.GetUInt32("req_exp", 0),
                    Tax = reader.GetInt64("tax", 0),
                    ConsumeLp = reader.GetInt32("consume_lp", 0)
                };
                _rndAttrElements[(element.CategoryId, element.Level)] = element;
            }
        }
    }
}
