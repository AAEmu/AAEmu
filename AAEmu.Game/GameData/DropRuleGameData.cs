using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads and reports drop-rule metadata for diagnostics. It does not select or generate loot;
/// the existing NPC loot path remains the sole runtime owner until the semantics are reviewed.
/// </summary>
[GameData]
public sealed class DropRuleGameData : Singleton<DropRuleGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, RuleBuilder> _rules = [];
    private readonly Dictionary<uint, DropRuleSubject> _npcSubjects = [];
    private readonly List<MissingLootPackMembership> _missingLootPackMemberships = [];
    private int _orphanMembershipCount;
    private DropRuleDefinition[] _compiledRules = [];
    private DropRuleDiagnostics _diagnostics = new(0, 0, 0, 0, [], 0, 0, 0);

    public IReadOnlyCollection<DropRuleDefinition> Rules => _compiledRules;
    public DropRuleDiagnostics Diagnostics => _diagnostics;

    public void Load(SqliteConnection connection)
    {
        _rules.Clear();
        _npcSubjects.Clear();
        _missingLootPackMemberships.Clear();
        _orphanMembershipCount = 0;
        LoadRules(connection);
        LoadMemberships(connection);
        LoadMissingLootPacks(connection);
        LoadNpcSubjects(connection);
        RebuildCompiledRules();
        RebuildDiagnostics();
    }

    public void PostLoad()
    {
        var format = "Drop-rule diagnostics: rules={0}, memberships={1}, orphan_memberships={2}, " +
                      "missing_pack_memberships={3}, missing_pack_ids={4}, invalid_rules={5}, " +
                      "for_batch_rules={6}, npc_subjects={7}";
        object[] values =
        [
            _diagnostics.RuleCount,
            _diagnostics.MembershipCount,
            _diagnostics.OrphanMembershipCount,
            _diagnostics.MissingLootPackMembershipCount,
            _diagnostics.MissingLootPackIds.Count,
            _diagnostics.InvalidRuleCount,
            _diagnostics.ForBatchRuleCount,
            _diagnostics.NpcSubjectCount
        ];
        if (_diagnostics.MissingLootPackMembershipCount > 0 || _diagnostics.InvalidRuleCount > 0)
            Logger.Warn(format, values);
        else
            Logger.Info(format, values);
    }

    public bool TryGetSubject(uint npcId, out DropRuleSubject subject) =>
        _npcSubjects.TryGetValue(npcId, out subject!);

    private void RebuildCompiledRules()
    {
        _compiledRules = _rules.Values
            .OrderBy(static rule => rule.Id)
            .Select(static rule => rule.Build())
            .ToArray();
    }

    private void RebuildDiagnostics()
    {
        var missingIds = _missingLootPackMemberships
            .Select(static membership => membership.LootPackId)
            .Distinct()
            .Order()
            .ToArray();
        _diagnostics = new DropRuleDiagnostics(
            _rules.Count,
            _rules.Values.Sum(static rule => rule.Memberships.Count),
            _orphanMembershipCount,
            _missingLootPackMemberships.Sum(static membership => membership.MembershipCount),
            missingIds,
            _rules.Values.Count(static rule => rule.InvalidReason is not null || !rule.Matcher.IsValid),
            _rules.Values.Count(static rule => rule.ForBatch),
            _npcSubjects.Count);
    }

    private void LoadRules(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT r.id, r.name, r.matcher_id, r.for_batch,
                   m.impl_type, m.impl_id, w.sql_where
            FROM drop_rules r
            LEFT JOIN matchers m ON m.id = r.matcher_id
            LEFT JOIN matcher_impl_sql_wheres w
              ON m.impl_type = 'MatcherImplSqlWhere' AND m.impl_id = w.id
            ORDER BY r.id ASC
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            if (!_rules.TryAdd(id, new RuleBuilder
                {
                    Id = id,
                    Name = reader.GetString("name", string.Empty),
                    MatcherId = reader.GetUInt32("matcher_id"),
                    ForBatch = reader.GetBoolean("for_batch", true)
                }))
            {
                throw new InvalidDataException($"Duplicate drop rule id {id}.");
            }

            var rule = _rules[id];
            if (reader.IsDBNull("impl_type") || reader.IsDBNull("impl_id") || reader.IsDBNull("sql_where"))
            {
                rule.InvalidReason = "matcher or SQL implementation is missing";
                continue;
            }

            if (!reader.GetString("impl_type").Equals("MatcherImplSqlWhere", StringComparison.Ordinal))
            {
                rule.InvalidReason = $"unsupported matcher implementation {reader.GetString("impl_type")}";
                continue;
            }

            if (!DropRuleMatcher.TryParse(reader.GetString("sql_where"), out var matcher, out var parseError) ||
                matcher is null)
            {
                rule.InvalidReason = $"matcher SQL is invalid: {parseError}";
                continue;
            }

            rule.Matcher = matcher;
        }
    }

    private void LoadMemberships(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, drop_rule_id, loot_pack_id FROM drop_rule_loot_packs ORDER BY drop_rule_id ASC, id ASC";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var ruleId = reader.GetUInt32("drop_rule_id");
            if (!_rules.TryGetValue(ruleId, out var rule))
            {
                _orphanMembershipCount++;
                Logger.Warn("Drop-rule membership {0} references missing rule {1}",
                    reader.GetUInt32("id"), ruleId);
                continue;
            }

            var packId = reader.GetUInt32("loot_pack_id");
            rule.Memberships.Add(new DropRuleMembership(reader.GetUInt32("id"), packId));
            if (packId == 0)
                rule.InvalidReason ??= "membership references loot pack 0";
        }

        foreach (var rule in _rules.Values)
            rule.InvalidReason ??= rule.Memberships.Count == 0 ? "rule has no loot-pack memberships" : null;
    }

    private void LoadMissingLootPacks(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.drop_rule_id, p.loot_pack_id, COUNT(*) AS membership_count
            FROM drop_rule_loot_packs p
            LEFT JOIN loot_packs lp ON lp.id = p.loot_pack_id
            WHERE lp.id IS NULL
            GROUP BY p.drop_rule_id, p.loot_pack_id
            ORDER BY p.drop_rule_id ASC, p.loot_pack_id ASC
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var membership = new MissingLootPackMembership(
                reader.GetUInt32("drop_rule_id"),
                reader.GetUInt32("loot_pack_id"),
                reader.GetInt32("membership_count"));
            _missingLootPackMemberships.Add(membership);
            if (_rules.TryGetValue(membership.RuleId, out var rule))
            {
                rule.InvalidReason ??=
                    $"membership references loot pack absent from loot_packs ({membership.LootPackId})";
            }
        }
    }

    private void LoadNpcSubjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, level, npc_tendency_id, npc_grade_id, npc_kind_id,
                   npc_nickname_id, heir_level, name, comment1, comment2, comment3, aggression
            FROM npcs
            ORDER BY id ASC
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            var subject = new DropRuleSubject(
                id,
                ReadNullableInt(reader, "level"),
                ReadNullableInt(reader, "npc_tendency_id"),
                ReadNullableInt(reader, "npc_grade_id"),
                ReadNullableInt(reader, "npc_kind_id"),
                ReadNullableInt(reader, "npc_nickname_id"),
                ReadNullableInt(reader, "heir_level"),
                ReadNullableString(reader, "name"),
                ReadNullableString(reader, "comment1"),
                ReadNullableString(reader, "comment2"),
                ReadNullableString(reader, "comment3"),
                ReadNullableBoolean(reader, "aggression"));
            if (!_npcSubjects.TryAdd(id, subject))
                throw new InvalidDataException($"Duplicate NPC id {id} while loading drop-rule subjects.");
        }
    }

    private static int? ReadNullableInt(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? null : reader.GetInt32(column);

    private static string? ReadNullableString(SQLiteWrapperReader reader, string column) =>
        reader.IsDBNull(column) ? null : reader.GetString(column);

    private static bool? ReadNullableBoolean(SQLiteWrapperReader reader, string column)
    {
        if (reader.IsDBNull(column))
            return null;
        return reader.GetBoolean(column, true);
    }

    private sealed record MissingLootPackMembership(uint RuleId, uint LootPackId, int MembershipCount);

    private sealed class RuleBuilder
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint MatcherId { get; init; }
        public bool ForBatch { get; init; }
        public DropRuleMatcher Matcher { get; set; } = DropRuleMatcher.Invalid;
        public List<DropRuleMembership> Memberships { get; } = [];
        public string? InvalidReason { get; set; }

        public DropRuleDefinition Build() => new()
        {
            Id = Id,
            Name = Name,
            MatcherId = MatcherId,
            ForBatch = ForBatch,
            Matcher = Matcher,
            Memberships = Memberships.ToArray(),
            InvalidReason = InvalidReason
        };
    }
}
