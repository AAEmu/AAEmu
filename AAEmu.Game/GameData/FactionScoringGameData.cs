using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>A row of <c>enum_faction_competition_reset_state_kinds</c>.</summary>
public sealed record FactionCompetitionResetState(uint Id, string Name);

/// <summary>One <c>faction_competitions</c> row, without any runtime score state.</summary>
public sealed record FactionCompetitionDefinition(
    uint Id,
    string Comments,
    int PlayerKillPoints,
    int NpcKillPoints,
    int QuestCompletePoints,
    int RequiredPoints,
    uint PointResetId,
    bool ForceChangeState,
    uint? ForceStopTowerDefId,
    string Tooltip,
    uint DetailId,
    string DetailType);

/// <summary>One <c>faction_competition_npc_infos</c> link row.</summary>
public sealed record FactionCompetitionNpcLink(uint Id, uint CompetitionId, uint NpcId);

/// <summary>One <c>faction_competition_quest_infos</c> link row.</summary>
public sealed record FactionCompetitionQuestLink(uint Id, uint CompetitionId, uint QuestContextId);

/// <summary>One <c>zone_score_contents</c> row.</summary>
public sealed record ZoneScoreContent(
    uint Id,
    string Name,
    bool ShowHud,
    uint ZoneGroupId,
    uint BuffId,
    uint QuestId);

/// <summary>One <c>zone_score_kinds</c> row.</summary>
public sealed record ZoneScoreKind(
    uint Id,
    uint ContentId,
    int UiOrder,
    bool DbSave,
    string ScoreName,
    uint IconId,
    int MaxScore,
    bool ShowScore,
    bool ShowMaxScore,
    string LevelName,
    bool ShowMaxLevel,
    bool ResetZoneIn,
    bool ResetZoneOut,
    bool ResetBuffDestroyed,
    bool ResetQuestRemoved);

/// <summary>One <c>zone_score_levels</c> row. Level zero is a shipped base row and is retained.</summary>
public sealed record ZoneScoreLevel(uint Id, uint KindId, int Level, long RequiredScore, uint BuffId);

/// <summary>One <c>zone_score_kind_rank_details</c> link row.</summary>
public sealed record ZoneScoreKindRankDetail(uint Id, uint ZoneScoreKindId);

/// <summary>
/// Read-only catalogs for faction competition and zone-score content.
/// </summary>
/// <remarks>
/// This loader deliberately stops at metadata. It does not award points, own a score table,
/// apply level thresholds, send a packet, or bridge World and Zone state. The shipped catalogs are
/// validated for parent/child integrity so a missing row fails during startup instead of being
/// silently treated as a zero or a default.
/// </remarks>
[GameData]
public sealed class FactionScoringGameData : Singleton<FactionScoringGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private FactionCompetitionResetState[] _resetStates = [];
    private FactionCompetitionDefinition[] _competitions = [];
    private FactionCompetitionNpcLink[] _npcLinks = [];
    private FactionCompetitionQuestLink[] _questLinks = [];
    private ZoneScoreContent[] _zoneScoreContents = [];
    private ZoneScoreKind[] _zoneScoreKinds = [];
    private ZoneScoreLevel[] _zoneScoreLevels = [];
    private ZoneScoreKindRankDetail[] _zoneScoreRankDetails = [];

    private Dictionary<uint, FactionCompetitionResetState> _resetStateById = [];
    private Dictionary<uint, FactionCompetitionDefinition> _competitionById = [];
    private Dictionary<uint, List<FactionCompetitionNpcLink>> _npcLinksByCompetition = [];
    private Dictionary<uint, List<FactionCompetitionQuestLink>> _questLinksByCompetition = [];
    private Dictionary<uint, ZoneScoreContent> _zoneScoreContentById = [];
    private Dictionary<uint, ZoneScoreKind> _zoneScoreKindById = [];
    private Dictionary<uint, List<ZoneScoreLevel>> _zoneScoreLevelsByKind = [];
    private Dictionary<uint, List<ZoneScoreKindRankDetail>> _zoneScoreRankDetailsByKind = [];

    public IReadOnlyList<FactionCompetitionResetState> ResetStates => _resetStates;
    public IReadOnlyList<FactionCompetitionDefinition> Competitions => _competitions;
    public IReadOnlyList<FactionCompetitionNpcLink> CompetitionNpcLinks => _npcLinks;
    public IReadOnlyList<FactionCompetitionQuestLink> CompetitionQuestLinks => _questLinks;
    public IReadOnlyList<ZoneScoreContent> ZoneScoreContents => _zoneScoreContents;
    public IReadOnlyList<ZoneScoreKind> ZoneScoreKinds => _zoneScoreKinds;
    public IReadOnlyList<ZoneScoreLevel> ZoneScoreLevels => _zoneScoreLevels;
    public IReadOnlyList<ZoneScoreKindRankDetail> ZoneScoreRankDetails => _zoneScoreRankDetails;

    public void Load(SqliteConnection connection)
    {
        var resetStates = LoadResetStates(connection);
        var resetStateById = ToUniqueMap(resetStates, "enum_faction_competition_reset_state_kinds");

        var competitions = LoadCompetitions(connection, resetStateById);
        var competitionById = ToUniqueMap(competitions, "faction_competitions");

        var npcIds = LoadNpcIds(connection);
        var questContextIds = LoadQuestContextIds(connection);
        var npcLinks = LoadNpcLinks(connection, competitionById, npcIds);
        var questLinks = LoadQuestLinks(connection, competitionById, questContextIds);

        var buffIds = LoadBuffIds(connection);
        var zoneScoreContents = LoadZoneScoreContents(connection, buffIds);
        var zoneScoreContentById = ToUniqueMap(zoneScoreContents, "zone_score_contents");

        var zoneScoreKinds = LoadZoneScoreKinds(connection, zoneScoreContentById);
        var zoneScoreKindById = ToUniqueMap(zoneScoreKinds, "zone_score_kinds");
        var zoneScoreLevels = LoadZoneScoreLevels(connection, zoneScoreKindById, buffIds);
        var zoneScoreLevelsByKind = GroupLevels(zoneScoreLevels, zoneScoreKindById);
        var zoneScoreRankDetails = LoadZoneScoreRankDetails(connection, zoneScoreKindById);
        var zoneScoreRankDetailsByKind = GroupRankDetails(zoneScoreRankDetails, zoneScoreKindById);

        _resetStates = resetStates;
        _resetStateById = resetStateById;
        _competitions = competitions;
        _competitionById = competitionById;
        _npcLinks = npcLinks;
        _npcLinksByCompetition = GroupNpcLinks(npcLinks, competitionById);
        _questLinks = questLinks;
        _questLinksByCompetition = GroupQuestLinks(questLinks, competitionById);
        _zoneScoreContents = zoneScoreContents;
        _zoneScoreContentById = zoneScoreContentById;
        _zoneScoreKinds = zoneScoreKinds;
        _zoneScoreKindById = zoneScoreKindById;
        _zoneScoreLevels = zoneScoreLevels;
        _zoneScoreLevelsByKind = zoneScoreLevelsByKind;
        _zoneScoreRankDetails = zoneScoreRankDetails;
        _zoneScoreRankDetailsByKind = zoneScoreRankDetailsByKind;

        Logger.Info("Loaded faction scoring metadata: {0} reset kinds, {1} competitions, {2} npc links, {3} quest links",
            resetStates.Length, competitions.Length, npcLinks.Length, questLinks.Length);
        Logger.Info("Loaded zone score metadata: {0} contents, {1} kinds, {2} levels, {3} rank links",
            zoneScoreContents.Length, zoneScoreKinds.Length, zoneScoreLevels.Length, zoneScoreRankDetails.Length);
    }

    public void PostLoad()
    {
    }

    public FactionCompetitionResetState GetResetState(uint id) =>
        _resetStateById.TryGetValue(id, out var value)
            ? value
            : throw MissingRow("enum_faction_competition_reset_state_kinds", id);

    public FactionCompetitionDefinition GetCompetition(uint id) =>
        _competitionById.TryGetValue(id, out var value)
            ? value
            : throw MissingRow("faction_competitions", id);

    public IReadOnlyList<FactionCompetitionNpcLink> GetNpcLinks(uint competitionId)
    {
        _ = GetCompetition(competitionId);
        return _npcLinksByCompetition.TryGetValue(competitionId, out var links) ? links : [];
    }

    public IReadOnlyList<FactionCompetitionQuestLink> GetQuestLinks(uint competitionId)
    {
        _ = GetCompetition(competitionId);
        return _questLinksByCompetition.TryGetValue(competitionId, out var links) ? links : [];
    }

    public ZoneScoreContent GetZoneScoreContent(uint id) =>
        _zoneScoreContentById.TryGetValue(id, out var value)
            ? value
            : throw MissingRow("zone_score_contents", id);

    public ZoneScoreKind GetZoneScoreKind(uint id) =>
        _zoneScoreKindById.TryGetValue(id, out var value)
            ? value
            : throw MissingRow("zone_score_kinds", id);

    public IReadOnlyList<ZoneScoreKind> GetZoneScoreKinds(uint contentId)
    {
        _ = GetZoneScoreContent(contentId);
        return _zoneScoreKinds.Where(kind => kind.ContentId == contentId).ToArray();
    }

    public IReadOnlyList<ZoneScoreLevel> GetZoneScoreLevels(uint kindId)
    {
        _ = GetZoneScoreKind(kindId);
        return _zoneScoreLevelsByKind.TryGetValue(kindId, out var levels) ? levels : [];
    }

    public IReadOnlyList<ZoneScoreKindRankDetail> GetZoneScoreRankDetails(uint kindId)
    {
        _ = GetZoneScoreKind(kindId);
        return _zoneScoreRankDetailsByKind.TryGetValue(kindId, out var links) ? links : [];
    }

    private static FactionCompetitionResetState[] LoadResetStates(SqliteConnection connection)
    {
        var rows = new List<FactionCompetitionResetState>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM enum_faction_competition_reset_state_kinds ORDER BY id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var name = reader.GetString("name");
            RequireNonZero(id, "enum_faction_competition_reset_state_kinds.id");
            RequireNonEmpty(name, "enum_faction_competition_reset_state_kinds.name", id);
            rows.Add(new FactionCompetitionResetState(id, name));
        }
        return rows.ToArray();
    }

    private static FactionCompetitionDefinition[] LoadCompetitions(
        SqliteConnection connection,
        Dictionary<uint, FactionCompetitionResetState> resetStates)
    {
        var rows = new List<FactionCompetitionDefinition>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, comments, point_pc_kill_value, point_npc_kill_value,
                   point_quest_complete_value, req_point, point_reset_id,
                   force_change_state, force_stop_tower_def_id, tooltip,
                   detail_id, detail_type
            FROM faction_competitions
            ORDER BY id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            RequireNonZero(id, "faction_competitions.id");
            var playerKillPoints = reader.GetInt32("point_pc_kill_value");
            var npcKillPoints = reader.GetInt32("point_npc_kill_value");
            var questCompletePoints = reader.GetInt32("point_quest_complete_value");
            var requiredPoints = reader.GetInt32("req_point");
            RequireNonNegative(playerKillPoints, "faction_competitions.point_pc_kill_value", id);
            RequireNonNegative(npcKillPoints, "faction_competitions.point_npc_kill_value", id);
            RequireNonNegative(questCompletePoints, "faction_competitions.point_quest_complete_value", id);
            RequireNonNegative(requiredPoints, "faction_competitions.req_point", id);

            var resetId = reader.GetUInt32("point_reset_id");
            if (!resetStates.ContainsKey(resetId))
                throw MissingRow("enum_faction_competition_reset_state_kinds", resetId, "faction_competitions.point_reset_id", id);

            var detailId = reader.GetUInt32("detail_id");
            var detailType = reader.GetString("detail_type");
            RequireNonZero(detailId, "faction_competitions.detail_id");
            RequireNonEmpty(detailType, "faction_competitions.detail_type", id);

            uint? forceStopTowerDefId = reader.IsDBNull("force_stop_tower_def_id")
                ? null
                : reader.GetUInt32("force_stop_tower_def_id");

            rows.Add(new FactionCompetitionDefinition(
                id,
                reader.GetString("comments"),
                playerKillPoints,
                npcKillPoints,
                questCompletePoints,
                requiredPoints,
                resetId,
                reader.GetBoolean("force_change_state"),
                forceStopTowerDefId,
                reader.GetString("tooltip"),
                detailId,
                detailType));
        }
        return rows.ToArray();
    }

    private static FactionCompetitionNpcLink[] LoadNpcLinks(
        SqliteConnection connection,
        Dictionary<uint, FactionCompetitionDefinition> competitions,
        HashSet<uint> npcIds)
    {
        var rows = new List<FactionCompetitionNpcLink>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, faction_competition_id, npc_id FROM faction_competition_npc_infos ORDER BY id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var competitionId = reader.GetUInt32("faction_competition_id");
            var npcId = reader.GetUInt32("npc_id");
            RequireNonZero(id, "faction_competition_npc_infos.id");
            RequireCompetition(competitions, competitionId, "faction_competition_npc_infos.faction_competition_id", id);
            RequireNpc(npcIds, npcId, id);
            rows.Add(new FactionCompetitionNpcLink(id, competitionId, npcId));
        }
        return rows.ToArray();
    }

    private static FactionCompetitionQuestLink[] LoadQuestLinks(
        SqliteConnection connection,
        Dictionary<uint, FactionCompetitionDefinition> competitions,
        HashSet<uint> questContextIds)
    {
        var rows = new List<FactionCompetitionQuestLink>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, faction_competition_id, context_id FROM faction_competition_quest_infos ORDER BY id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var competitionId = reader.GetUInt32("faction_competition_id");
            var contextId = reader.GetUInt32("context_id");
            RequireNonZero(id, "faction_competition_quest_infos.id");
            RequireCompetition(competitions, competitionId, "faction_competition_quest_infos.faction_competition_id", id);
            if (contextId == 0 || !questContextIds.Contains(contextId))
                throw MissingRow("quest_contexts", contextId, "faction_competition_quest_infos.context_id", id);
            rows.Add(new FactionCompetitionQuestLink(id, competitionId, contextId));
        }
        return rows.ToArray();
    }

    private static ZoneScoreContent[] LoadZoneScoreContents(SqliteConnection connection, HashSet<uint> buffIds)
    {
        var rows = new List<ZoneScoreContent>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, show_hud, zone_group_id, buff_id, quest_id FROM zone_score_contents ORDER BY id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var zoneGroupId = reader.GetUInt32("zone_group_id");
            var buffId = reader.GetUInt32("buff_id");
            RequireNonZero(id, "zone_score_contents.id");
            RequireNonZero(zoneGroupId, "zone_score_contents.zone_group_id", id);
            RequireBuff(buffIds, buffId, "zone_score_contents.buff_id", id);
            rows.Add(new ZoneScoreContent(
                id,
                reader.GetString("name"),
                reader.GetBoolean("show_hud"),
                zoneGroupId,
                buffId,
                reader.GetUInt32("quest_id")));
        }
        return rows.ToArray();
    }

    private static ZoneScoreKind[] LoadZoneScoreKinds(
        SqliteConnection connection,
        Dictionary<uint, ZoneScoreContent> contents)
    {
        var rows = new List<ZoneScoreKind>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, content_id, ui_order, db_save, score_name, icon_id, max_score,
                   show_score, show_max_score, level_name, show_max_level,
                   reset_zone_in, reset_zone_out, reset_buff_destroyed, reset_quest_removed
            FROM zone_score_kinds
            ORDER BY id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var contentId = reader.GetUInt32("content_id");
            var uiOrder = reader.GetInt32("ui_order");
            var maxScore = reader.GetInt32("max_score");
            RequireNonZero(id, "zone_score_kinds.id");
            if (!contents.ContainsKey(contentId))
                throw MissingRow("zone_score_contents", contentId, "zone_score_kinds.content_id", id);
            RequireNonNegative(uiOrder, "zone_score_kinds.ui_order", id);
            RequireNonNegative(maxScore, "zone_score_kinds.max_score", id);

            rows.Add(new ZoneScoreKind(
                id,
                contentId,
                uiOrder,
                reader.GetBoolean("db_save"),
                reader.GetString("score_name"),
                reader.GetUInt32("icon_id"),
                maxScore,
                reader.GetBoolean("show_score"),
                reader.GetBoolean("show_max_score"),
                reader.GetString("level_name"),
                reader.GetBoolean("show_max_level"),
                reader.GetBoolean("reset_zone_in"),
                reader.GetBoolean("reset_zone_out"),
                reader.GetBoolean("reset_buff_destroyed"),
                reader.GetBoolean("reset_quest_removed")));
        }
        return rows.ToArray();
    }

    private static ZoneScoreLevel[] LoadZoneScoreLevels(
        SqliteConnection connection,
        Dictionary<uint, ZoneScoreKind> kinds,
        HashSet<uint> buffIds)
    {
        var rows = new List<ZoneScoreLevel>();
        var keys = new HashSet<(uint KindId, int Level)>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind_id, level, req_score, buff_id FROM zone_score_levels ORDER BY kind_id, level, id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var kindId = reader.GetUInt32("kind_id");
            var level = reader.GetInt32("level");
            var requiredScore = reader.GetInt64("req_score");
            var buffId = reader.GetUInt32("buff_id");
            RequireNonZero(id, "zone_score_levels.id");
            if (!kinds.ContainsKey(kindId))
                throw MissingRow("zone_score_kinds", kindId, "zone_score_levels.kind_id", id);
            RequireNonNegative(level, "zone_score_levels.level", id);
            RequireNonNegative(requiredScore, "zone_score_levels.req_score", id);
            RequireBuff(buffIds, buffId, "zone_score_levels.buff_id", id);
            if (!keys.Add((kindId, level)))
                throw new InvalidOperationException($"zone_score_levels contains duplicate kind {kindId} level {level}.");
            rows.Add(new ZoneScoreLevel(id, kindId, level, requiredScore, buffId));
        }
        return rows.ToArray();
    }

    private static ZoneScoreKindRankDetail[] LoadZoneScoreRankDetails(
        SqliteConnection connection,
        Dictionary<uint, ZoneScoreKind> kinds)
    {
        var rows = new List<ZoneScoreKindRankDetail>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, zone_score_kind_id FROM zone_score_kind_rank_details ORDER BY id";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var kindId = reader.GetUInt32("zone_score_kind_id");
            RequireNonZero(id, "zone_score_kind_rank_details.id");
            if (!kinds.ContainsKey(kindId))
                throw MissingRow("zone_score_kinds", kindId, "zone_score_kind_rank_details.zone_score_kind_id", id);
            rows.Add(new ZoneScoreKindRankDetail(id, kindId));
        }
        return rows.ToArray();
    }

    private static HashSet<uint> LoadNpcIds(SqliteConnection connection) =>
        LoadIds(connection, "SELECT id FROM npcs");

    private static HashSet<uint> LoadBuffIds(SqliteConnection connection) =>
        LoadIds(connection, "SELECT id FROM buffs");

    private static HashSet<uint> LoadQuestContextIds(SqliteConnection connection) =>
        LoadIds(connection, "SELECT id FROM quest_contexts");

    private static HashSet<uint> LoadIds(SqliteConnection connection, string commandText)
    {
        var ids = new HashSet<uint>();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            ids.Add(reader.GetUInt32("id"));
        return ids;
    }

    private static Dictionary<uint, T> ToUniqueMap<T>(IReadOnlyList<T> rows, string table) where T : class
    {
        var map = new Dictionary<uint, T>();
        foreach (var row in rows)
        {
            var id = row switch
            {
                FactionCompetitionResetState value => value.Id,
                FactionCompetitionDefinition value => value.Id,
                ZoneScoreContent value => value.Id,
                ZoneScoreKind value => value.Id,
                _ => throw new ArgumentOutOfRangeException(nameof(row))
            };
            if (!map.TryAdd(id, row))
                throw new InvalidOperationException($"{table} contains duplicate id {id}.");
        }
        return map;
    }

    private static Dictionary<uint, List<FactionCompetitionNpcLink>> GroupNpcLinks(
        IReadOnlyList<FactionCompetitionNpcLink> links,
        Dictionary<uint, FactionCompetitionDefinition> competitions)
    {
        var grouped = new Dictionary<uint, List<FactionCompetitionNpcLink>>();
        foreach (var link in links)
        {
            RequireCompetition(competitions, link.CompetitionId, "faction_competition_npc_infos.faction_competition_id", link.Id);
            if (!grouped.TryGetValue(link.CompetitionId, out var list))
                grouped[link.CompetitionId] = list = [];
            list.Add(link);
        }
        return grouped;
    }

    private static Dictionary<uint, List<FactionCompetitionQuestLink>> GroupQuestLinks(
        IReadOnlyList<FactionCompetitionQuestLink> links,
        Dictionary<uint, FactionCompetitionDefinition> competitions)
    {
        var grouped = new Dictionary<uint, List<FactionCompetitionQuestLink>>();
        foreach (var link in links)
        {
            RequireCompetition(competitions, link.CompetitionId, "faction_competition_quest_infos.faction_competition_id", link.Id);
            if (!grouped.TryGetValue(link.CompetitionId, out var list))
                grouped[link.CompetitionId] = list = [];
            list.Add(link);
        }
        return grouped;
    }

    private static Dictionary<uint, List<ZoneScoreLevel>> GroupLevels(
        IReadOnlyList<ZoneScoreLevel> levels,
        Dictionary<uint, ZoneScoreKind> kinds)
    {
        var grouped = new Dictionary<uint, List<ZoneScoreLevel>>();
        foreach (var level in levels)
        {
            if (!kinds.ContainsKey(level.KindId))
                throw MissingRow("zone_score_kinds", level.KindId, "zone_score_levels.kind_id", level.Id);
            if (!grouped.TryGetValue(level.KindId, out var list))
                grouped[level.KindId] = list = [];
            list.Add(level);
        }

        foreach (var kindId in kinds.Keys)
        {
            if (!grouped.ContainsKey(kindId))
                throw new InvalidOperationException($"zone_score_kinds {kindId} has no zone_score_levels row.");
        }
        return grouped;
    }

    private static Dictionary<uint, List<ZoneScoreKindRankDetail>> GroupRankDetails(
        IReadOnlyList<ZoneScoreKindRankDetail> links,
        Dictionary<uint, ZoneScoreKind> kinds)
    {
        var grouped = new Dictionary<uint, List<ZoneScoreKindRankDetail>>();
        foreach (var link in links)
        {
            if (!kinds.ContainsKey(link.ZoneScoreKindId))
                throw MissingRow("zone_score_kinds", link.ZoneScoreKindId, "zone_score_kind_rank_details.zone_score_kind_id", link.Id);
            if (!grouped.TryGetValue(link.ZoneScoreKindId, out var list))
                grouped[link.ZoneScoreKindId] = list = [];
            list.Add(link);
        }
        return grouped;
    }

    private static void RequireCompetition(
        Dictionary<uint, FactionCompetitionDefinition> competitions,
        uint competitionId,
        string column,
        uint linkId)
    {
        if (!competitions.ContainsKey(competitionId))
            throw MissingRow("faction_competitions", competitionId, column, linkId);
    }

    private static void RequireNpc(HashSet<uint> npcIds, uint npcId, uint linkId)
    {
        if (npcId == 0 || !npcIds.Contains(npcId))
            throw MissingRow("npcs", npcId, "faction_competition_npc_infos.npc_id", linkId);
    }

    private static void RequireBuff(HashSet<uint> buffIds, uint buffId, string column, uint rowId)
    {
        if (buffId != 0 && !buffIds.Contains(buffId))
            throw MissingRow("buffs", buffId, column, rowId);
    }

    private static void RequireNonZero(uint value, string column, uint? rowId = null)
    {
        if (value == 0)
            throw new InvalidOperationException($"{column} must be non-zero{(rowId.HasValue ? $" (row {rowId.Value})" : string.Empty)}.");
    }

    private static void RequireNonNegative(int value, string column, uint rowId)
    {
        if (value < 0)
            throw new InvalidOperationException($"{column} must be non-negative (row {rowId}).");
    }

    private static void RequireNonNegative(long value, string column, uint rowId)
    {
        if (value < 0)
            throw new InvalidOperationException($"{column} must be non-negative (row {rowId}).");
    }

    private static void RequireNonEmpty(string value, string column, uint rowId)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{column} must not be empty (row {rowId}).");
    }

    private static KeyNotFoundException MissingRow(string table, uint id, string sourceColumn = null, uint? sourceRowId = null)
    {
        var source = sourceColumn is null
            ? string.Empty
            : $" referenced by {sourceColumn}{(sourceRowId.HasValue ? $" row {sourceRowId.Value}" : string.Empty)}";
        return new KeyNotFoundException($"{table} has no row with id {id}{source}.");
    }
}
