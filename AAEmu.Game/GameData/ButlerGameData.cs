using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Farmhand static content from the 10.0.2.13 game database. Runtime ownership, purchased expansions,
/// and jobs are deliberately outside this loader.
/// </summary>
[GameData]
public class ButlerGameData : Singleton<ButlerGameData>, IGameDataLoader
{
    private Dictionary<uint, ButlerTemplate> _templatesById = [];
    private Dictionary<(uint ButlerId, uint Level), ButlerLevel> _levelsByButlerAndLevel = [];
    private Dictionary<uint, List<ButlerLevel>> _levelsByButlerId = [];
    private Dictionary<uint, ButlerHarvestGrade> _harvestGradesById = [];
    private Dictionary<uint, ButlerHarvest> _harvestsById = [];
    private Dictionary<uint, List<ButlerHarvest>> _harvestsByGradeId = [];
    private Dictionary<(uint ButlerId, uint Level), ButlerSlotExpansion> _gardenExpansionsByButlerAndLevel = [];
    private Dictionary<(uint ButlerId, uint TotalExpandSlotCount), ButlerSlotExpansion>
        _gardenExpansionsByButlerAndTotalCount = [];
    private Dictionary<(uint ButlerId, uint Level), ButlerSlotExpansion> _tradeExpansionsByButlerAndLevel = [];
    private Dictionary<(uint ButlerId, uint TotalExpandSlotCount), ButlerSlotExpansion>
        _tradeExpansionsByButlerAndTotalCount = [];
    private Dictionary<uint, ButlerSpecialtyTradeDefinition> _specialtyTradesById = [];
    private Dictionary<(uint CraftId, uint ZoneGroupId), ButlerSpecialtyTradeDefinition> _specialtyTradesByCraftAndZone = [];
    private Dictionary<uint, ButlerBindingDoodadFunc> _bindingDoodadFuncsById = [];

    public void Load(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _templatesById = [];
        _levelsByButlerAndLevel = [];
        _levelsByButlerId = [];
        _harvestGradesById = [];
        _harvestsById = [];
        _harvestsByGradeId = [];
        _gardenExpansionsByButlerAndLevel = [];
        _gardenExpansionsByButlerAndTotalCount = [];
        _tradeExpansionsByButlerAndLevel = [];
        _tradeExpansionsByButlerAndTotalCount = [];
        _specialtyTradesById = [];
        _specialtyTradesByCraftAndZone = [];
        _bindingDoodadFuncsById = [];

        LoadTemplates(connection);
        LoadLevels(connection);
        LoadSlotExpansions(connection, "butler_func_garden_expand_slots", _gardenExpansionsByButlerAndLevel,
            _gardenExpansionsByButlerAndTotalCount);
        LoadSlotExpansions(connection, "butler_func_trade_expand_slots", _tradeExpansionsByButlerAndLevel,
            _tradeExpansionsByButlerAndTotalCount);
        LoadHarvestGrades(connection);
        LoadHarvests(connection);
        LoadSpecialtyTrades(connection);
        LoadBindingDoodadFuncs(connection);
    }

    public void PostLoad()
    {
    }

    public bool TryGetTemplate(uint butlerId, out ButlerTemplate template) =>
        _templatesById.TryGetValue(butlerId, out template);

    /// <summary>Gets the only configured farmhand template, failing closed when content has more than one.</summary>
    public bool TryGetUniqueTemplate(out ButlerTemplate template)
    {
        template = null;
        if (_templatesById.Count != 1)
            return false;

        template = _templatesById.Values.Single();
        return template.Id != 0;
    }

    public bool TryGetLevel(uint butlerId, uint level, out ButlerLevel template) =>
        _levelsByButlerAndLevel.TryGetValue((butlerId, level), out template);

    /// <summary>Resolves cumulative experience to the highest usable Farmhand level.</summary>
    public bool TryGetLevelForCumulativeExperience(uint butlerId, ulong cumulativeExperience,
        out ButlerLevel template)
    {
        template = null;
        if (!_levelsByButlerId.TryGetValue(butlerId, out var levels))
            return false;

        foreach (var candidate in levels)
        {
            if (candidate.Level > ButlerProgression.MaximumUsableLevel || candidate.TotalExp < 0 ||
                (ulong)candidate.TotalExp > cumulativeExperience)
                continue;
            if (template == null || candidate.Level > template.Level)
                template = candidate;
        }

        return template != null;
    }

    /// <summary>Gets the maximum usable Farmhand level for this client version.</summary>
    public bool TryGetMaximumLevel(uint butlerId, out ButlerLevel template)
    {
        template = null;
        if (!_levelsByButlerId.TryGetValue(butlerId, out var levels))
            return false;

        foreach (var candidate in levels)
            if (candidate.Level <= ButlerProgression.MaximumUsableLevel &&
                (template == null || candidate.Level > template.Level))
                template = candidate;
        return template != null;
    }

    /// <summary>
    /// Gets the experience threshold immediately after the usable Farmhand cap. The target content stores that
    /// threshold on level 41, while the client exposes level 40 as the cap.
    /// </summary>
    public bool TryGetNextLevelExperienceThreshold(uint butlerId, out long totalExperience)
    {
        totalExperience = 0;
        if (!_levelsByButlerAndLevel.TryGetValue((butlerId, ButlerProgression.MaximumUsableLevel + 1),
                out var sentinel) || sentinel.TotalExp < 0)
            return false;

        totalExperience = sentinel.TotalExp;
        return true;
    }

    public bool TryGetHarvestGrade(uint harvestGradeId, out ButlerHarvestGrade template) =>
        _harvestGradesById.TryGetValue(harvestGradeId, out template);

    public bool TryGetHarvest(uint harvestId, out ButlerHarvest template) =>
        _harvestsById.TryGetValue(harvestId, out template);

    public IReadOnlyList<ButlerHarvest> GetHarvests(uint harvestGradeId) =>
        _harvestsByGradeId.GetValueOrDefault(harvestGradeId) ?? [];

    public bool TryGetSpecialtyTrade(uint tradeId, ushort zoneGroupId,
        out ButlerSpecialtyTradeDefinition trade)
    {
        trade = null;
        return _specialtyTradesById.TryGetValue(tradeId, out trade) &&
               trade.ZoneGroupId == zoneGroupId;
    }

    /// <summary>Looks up the raw <c>level</c> key from <c>butler_func_garden_expand_slots</c>.</summary>
    public bool TryGetGardenSlotExpansion(uint butlerId, uint level, out ButlerSlotExpansion expansion) =>
        _gardenExpansionsByButlerAndLevel.TryGetValue((butlerId, level), out expansion);

    /// <summary>Looks up the raw <c>total_expand_slot_count</c> key from the garden-expansion table.</summary>
    public bool TryGetGardenSlotExpansionByTotalCount(uint butlerId, uint totalExpandSlotCount,
        out ButlerSlotExpansion expansion) =>
        _gardenExpansionsByButlerAndTotalCount.TryGetValue((butlerId, totalExpandSlotCount), out expansion);

    /// <summary>Looks up the raw <c>level</c> key from <c>butler_func_trade_expand_slots</c>.</summary>
    public bool TryGetTradeSlotExpansion(uint butlerId, uint level, out ButlerSlotExpansion expansion) =>
        _tradeExpansionsByButlerAndLevel.TryGetValue((butlerId, level), out expansion);

    /// <summary>Looks up the raw <c>total_expand_slot_count</c> key from the trade-expansion table.</summary>
    public bool TryGetTradeSlotExpansionByTotalCount(uint butlerId, uint totalExpandSlotCount,
        out ButlerSlotExpansion expansion) =>
        _tradeExpansionsByButlerAndTotalCount.TryGetValue((butlerId, totalExpandSlotCount), out expansion);

    public bool TryGetBindingDoodadFunc(uint doodadFuncId, out ButlerBindingDoodadFunc descriptor) =>
        _bindingDoodadFuncsById.TryGetValue(doodadFuncId, out descriptor);

    public bool IsBindingDoodadFunc(uint doodadFuncId) => _bindingDoodadFuncsById.ContainsKey(doodadFuncId);

    private void LoadTemplates(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, model_id, default_garden_slot_count, reset_all_actability_cost,
                   reset_all_actability_currency_id, lp_charge_rate, max_production_cost, default_fx_group_id,
                   trade_available_level, overwork_production_cost_mul, default_specialty_trade_slot_count
            FROM butlers
            """;
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new ButlerTemplate
            {
                Id = reader.GetUInt32("id"),
                Name = reader.GetString("name"),
                ModelId = reader.GetUInt32("model_id"),
                DefaultGardenSlotCount = GetNullableUInt32(reader, "default_garden_slot_count"),
                ResetAllActabilityCost = GetNullableUInt32(reader, "reset_all_actability_cost"),
                ResetAllActabilityCurrencyId = GetNullableUInt32(reader, "reset_all_actability_currency_id"),
                LpChargeRate = GetNullableUInt32(reader, "lp_charge_rate"),
                MaxProductionCost = GetNullableUInt32(reader, "max_production_cost"),
                DefaultFxGroupId = reader.GetUInt32("default_fx_group_id"),
                TradeAvailableLevel = reader.GetUInt32("trade_available_level"),
                OverworkProductionCostMul = reader.GetUInt32("overwork_production_cost_mul"),
                DefaultSpecialtyTradeSlotCount = reader.GetUInt32("default_specialty_trade_slot_count")
            };
            _templatesById[template.Id] = template;
        }
    }

    private void LoadLevels(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, butler_id, level, max_labor_power, max_stat_point, total_exp, effect_desc,
                   total_garden_count, butler_harvest_grade_id
            FROM butler_levels
            """;
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new ButlerLevel
            {
                Id = reader.GetUInt32("id"),
                ButlerId = reader.GetUInt32("butler_id"),
                Level = reader.GetUInt32("level"),
                MaxLaborPower = reader.GetUInt32("max_labor_power"),
                MaxStatPoint = reader.GetUInt32("max_stat_point"),
                TotalExp = reader.GetInt64("total_exp"),
                EffectDesc = GetNullableString(reader, "effect_desc"),
                TotalGardenCount = reader.GetUInt32("total_garden_count"),
                ButlerHarvestGradeId = reader.GetUInt32("butler_harvest_grade_id")
            };
            _levelsByButlerAndLevel[(template.ButlerId, template.Level)] = template;
            if (!_levelsByButlerId.TryGetValue(template.ButlerId, out var levels))
                _levelsByButlerId[template.ButlerId] = levels = [];
            levels.Add(template);
        }
    }

    private static void LoadSlotExpansions(SqliteConnection connection, string table,
        IDictionary<(uint ButlerId, uint Level), ButlerSlotExpansion> byLevel,
        IDictionary<(uint ButlerId, uint TotalExpandSlotCount), ButlerSlotExpansion> byTotalCount)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, butler_id, level, total_expand_slot_count, require_item_id, require_item_count FROM {table}";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var expansion = new ButlerSlotExpansion
            {
                Id = reader.GetUInt32("id"),
                ButlerId = reader.GetUInt32("butler_id"),
                Level = reader.GetUInt32("level"),
                TotalExpandSlotCount = reader.GetUInt32("total_expand_slot_count"),
                RequireItemId = reader.GetUInt32("require_item_id"),
                RequireItemCount = reader.GetUInt32("require_item_count")
            };
            byLevel[(expansion.ButlerId, expansion.Level)] = expansion;
            byTotalCount[(expansion.ButlerId, expansion.TotalExpandSlotCount)] = expansion;
        }
    }

    private void LoadHarvestGrades(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, grade, \"desc\" FROM butler_harvest_grades";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var grade = new ButlerHarvestGrade
            {
                Id = reader.GetUInt32("id"),
                Grade = reader.GetUInt32("grade"),
                Description = reader.GetString("desc")
            };
            _harvestGradesById[grade.Id] = grade;
        }
    }

    private void LoadHarvests(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, item_id, growth_time, size, repeat_count, actability_group_id, consume_lp, loot_pack_id,
                   bonus_ratio, bonus_loot_pack_id, butler_harvest_grade_id, is_under_water
            FROM butler_harvests
            """;
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var harvest = new ButlerHarvest
            {
                Id = reader.GetUInt32("id"),
                ItemId = GetNullableUInt32(reader, "item_id"),
                GrowthTime = GetNullableUInt32(reader, "growth_time"),
                Size = GetNullableUInt32(reader, "size"),
                RepeatCount = GetNullableUInt32(reader, "repeat_count"),
                ActabilityGroupId = GetNullableUInt32(reader, "actability_group_id"),
                ConsumeLp = GetNullableUInt32(reader, "consume_lp"),
                LootPackId = GetNullableUInt32(reader, "loot_pack_id"),
                BonusRatio = GetNullableUInt32(reader, "bonus_ratio"),
                BonusLootPackId = GetNullableUInt32(reader, "bonus_loot_pack_id"),
                ButlerHarvestGradeId = reader.GetUInt32("butler_harvest_grade_id"),
                IsUnderWater = GetNullableBoolean(reader, "is_under_water")
            };
            _harvestsById[harvest.Id] = harvest;
            if (!_harvestsByGradeId.TryGetValue(harvest.ButlerHarvestGradeId, out var harvests))
                _harvestsByGradeId[harvest.ButlerHarvestGradeId] = harvests = [];
            harvests.Add(harvest);
        }
    }

    private void LoadSpecialtyTrades(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.id, t.npc_id, t.craft_id, t.delivery_min_time, t.delivery_max_time,
                   t.consume_production_cost, n.zone_group_id
            FROM butler_specialty_trades t
            INNER JOIN specialty_npcs n ON n.npc_id = t.npc_id
            """;
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var trade = new ButlerSpecialtyTradeDefinition(
                reader.GetUInt32("id"),
                reader.GetUInt32("npc_id"),
                reader.GetUInt32("craft_id"),
                reader.GetUInt32("delivery_min_time"),
                reader.GetUInt32("delivery_max_time"),
                reader.GetUInt32("consume_production_cost"),
                reader.GetUInt32("zone_group_id"));
            if (trade.Id == 0 || trade.NpcId == 0 || trade.CraftId == 0 || trade.ZoneGroupId == 0 ||
                trade.ZoneGroupId > ushort.MaxValue || trade.DeliveryMinTime == 0 ||
                trade.DeliveryMinTime > trade.DeliveryMaxTime || trade.ConsumeProductionCost == 0)
                throw new InvalidDataException("butler_specialty_trades contains an invalid row.");
            if (!_specialtyTradesById.TryAdd(trade.Id, trade))
                throw new InvalidDataException($"butler_specialty_trades has duplicate id {trade.Id}.");
            if (!_specialtyTradesByCraftAndZone.TryAdd((trade.CraftId, trade.ZoneGroupId), trade))
                throw new InvalidDataException(
                    $"butler_specialty_trades has duplicate craft/zone key {trade.CraftId}/{trade.ZoneGroupId}.");
        }
    }

    private void LoadBindingDoodadFuncs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM doodad_func_bind_butlers";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var descriptor = new ButlerBindingDoodadFunc { Id = reader.GetUInt32("id") };
            _bindingDoodadFuncsById[descriptor.Id] = descriptor;
        }
    }

    private static uint? GetNullableUInt32(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? null : reader.GetUInt32(column);

    private static string? GetNullableString(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? null : reader.GetString(column);

    private static bool? GetNullableBoolean(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? null : reader.GetBoolean(column);
}
