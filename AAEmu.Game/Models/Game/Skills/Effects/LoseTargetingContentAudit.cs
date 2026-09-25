using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>The content link tables that can name a type-146 special-effect row.</summary>
public enum LoseTargetingLinkSource
{
    SkillEffect,
    BuffTrigger,
    BuffTickEffect,
    PlotEffect,
}

/// <summary>
/// The two link interpretations kept separate by the audit. Plot rows are content links, but they are
/// not evidence that a World-side skill/buff path exists.
/// </summary>
public enum LoseTargetingLinkScope
{
    /// <summary>Links reachable through the server's skill, buff-trigger and buff-tick effect loaders.</summary>
    ServerEffectLinks,

    /// <summary>All content links, including plot-owned rows.</summary>
    AllContentLinks,
}

/// <summary>One validated <c>special_effects</c> row for the type under audit.</summary>
public sealed record LoseTargetingContentRow(uint Id, SpecialType Type, int Value1, int Value2);

/// <summary>One content link to a type-146 row. The link table is retained for diagnostics only.</summary>
public sealed record LoseTargetingContentLink(uint ContentId, LoseTargetingLinkSource Source)
{
    public bool IsIn(LoseTargetingLinkScope scope) => scope == LoseTargetingLinkScope.AllContentLinks ||
        Source != LoseTargetingLinkSource.PlotEffect;
}

/// <summary>
/// A reviewed count baseline for the audit. Counts are supplied by the caller; production loading does
/// not embed a shipped row count or an orphan-id list.
/// </summary>
public sealed record LoseTargetingAuditBaseline(
    int ContentRows,
    int LinkedRows,
    LoseTargetingLinkScope LinkScope)
{
    public int UnlinkedRows => ContentRows - LinkedRows;
}

/// <summary>The typed result of a content-only audit. It never enumerates units or changes targeting.</summary>
public sealed class LoseTargetingContentAuditResult
{
    private readonly HashSet<uint> _contentIds;
    private readonly HashSet<uint> _serverLinkedIds;
    private readonly HashSet<uint> _allLinkedIds;
    private readonly Dictionary<LoseTargetingLinkSource, int> _linkCounts;

    private LoseTargetingContentAuditResult(
        IReadOnlyList<LoseTargetingContentRow> rows,
        IReadOnlyList<LoseTargetingContentLink> links)
    {
        Rows = rows;
        Links = links;
        _contentIds = rows.Select(row => row.Id).ToHashSet();
        _serverLinkedIds = links.Where(link => link.IsIn(LoseTargetingLinkScope.ServerEffectLinks))
            .Select(link => link.ContentId).ToHashSet();
        _allLinkedIds = links.Select(link => link.ContentId).ToHashSet();
        _linkCounts = links.GroupBy(link => link.Source)
            .ToDictionary(group => group.Key, group => group.Select(link => link.ContentId).Distinct().Count());
    }

    public IReadOnlyList<LoseTargetingContentRow> Rows { get; }
    public IReadOnlyList<LoseTargetingContentLink> Links { get; }
    public int ContentRowCount => Rows.Count;
    public IReadOnlyCollection<uint> ContentIds => _contentIds;

    public int LinkedRowCount(LoseTargetingLinkScope scope) => LinkedIds(scope).Count;

    public IReadOnlyCollection<uint> LinkedIds(LoseTargetingLinkScope scope) =>
        scope == LoseTargetingLinkScope.AllContentLinks ? _allLinkedIds : _serverLinkedIds;

    public IReadOnlyCollection<uint> UnlinkedIds(LoseTargetingLinkScope scope) =>
        _contentIds.Except(LinkedIds(scope)).OrderBy(id => id).ToArray();

    public IReadOnlyDictionary<LoseTargetingLinkSource, int> LinkCounts => _linkCounts;

    /// <summary>
    /// Compares the observed matrix with a reviewed baseline. Any count drift, including a new orphan,
    /// is an explicit failure; this is used by the content audit test, not by normal server startup.
    /// </summary>
    public void ValidateAgainst(LoseTargetingAuditBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var linked = LinkedRowCount(baseline.LinkScope);
        var unlinked = ContentRowCount - linked;
        if (ContentRowCount != baseline.ContentRows ||
            linked != baseline.LinkedRows ||
            unlinked != baseline.UnlinkedRows)
        {
            throw new InvalidDataException(
                $"GF-C08 LoseTargeting content matrix changed: content={ContentRowCount} " +
                $"(expected {baseline.ContentRows}), linked[{baseline.LinkScope}]={linked} " +
                $"(expected {baseline.LinkedRows}), unlinked={unlinked} " +
                $"(expected {baseline.UnlinkedRows}).");
        }
    }

    internal static LoseTargetingContentAuditResult Create(
        IEnumerable<LoseTargetingContentRow> rows,
        IEnumerable<LoseTargetingContentLink> links)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(links);

        var rowList = rows.ToList();
        var linkList = links.ToList();
        var contentIds = new HashSet<uint>();
        foreach (var row in rowList)
        {
            if (row.Type != SpecialType.LoseTargetingTheTarget)
            {
                throw new InvalidDataException(
                    $"GF-C08 content audit received special effect {row.Id} of type {row.Type}, " +
                    $"expected {SpecialType.LoseTargetingTheTarget}.");
            }

            if (!contentIds.Add(row.Id))
                throw new InvalidDataException($"GF-C08 content audit found duplicate special effect row {row.Id}.");
        }

        foreach (var link in linkList)
        {
            if (!contentIds.Contains(link.ContentId))
            {
                throw new InvalidDataException(
                    $"GF-C08 content audit link {link.Source} names unknown special effect row {link.ContentId}.");
            }
        }

        return new LoseTargetingContentAuditResult(rowList, linkList);
    }
}

/// <summary>
/// Reads and reports the type-146 content/link matrix. This is deliberately a diagnostic only: it does
/// not enumerate targets, choose a target, or alter the existing special-effect action.
/// </summary>
public static class LoseTargetingContentAudit
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Loads the typed matrix from the open content connection.</summary>
    public static LoseTargetingContentAuditResult Read(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var rows = new List<LoseTargetingContentRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, special_effect_type_id, value1, value2 " +
                "FROM special_effects WHERE special_effect_type_id = @type";
            command.Parameters.AddWithValue("@type", (int)SpecialType.LoseTargetingTheTarget);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                    throw new InvalidDataException("GF-C08 special_effects audit found a null required field.");

                rows.Add(new LoseTargetingContentRow(
                    Convert.ToUInt32(reader.GetValue(0)),
                    (SpecialType)Convert.ToInt32(reader.GetValue(1)),
                    Convert.ToInt32(reader.GetValue(2)),
                    Convert.ToInt32(reader.GetValue(3))));
            }
        }

        var links = new List<LoseTargetingContentLink>();
        AddServerLinks(connection, links, LoseTargetingLinkSource.SkillEffect,
            "skill_effects", requireEnabled: true);
        AddServerLinks(connection, links, LoseTargetingLinkSource.BuffTrigger,
            "buff_triggers", requireEnabled: true);
        AddServerLinks(connection, links, LoseTargetingLinkSource.BuffTickEffect,
            "buff_tick_effects", requireEnabled: false);

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT DISTINCT actual_id FROM plot_effects " +
                "WHERE actual_type IN ('SpecialEffect', 'BuffEffect') " +
                "AND actual_id IN (SELECT id FROM special_effects WHERE special_effect_type_id = @type)";
            command.Parameters.AddWithValue("@type", (int)SpecialType.LoseTargetingTheTarget);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                    throw new InvalidDataException("GF-C08 plot_effects audit found a null actual_id.");
                links.Add(new LoseTargetingContentLink(Convert.ToUInt32(reader.GetValue(0)),
                    LoseTargetingLinkSource.PlotEffect));
            }
        }

        return LoseTargetingContentAuditResult.Create(rows, links);
    }

    /// <summary>Reads the matrix and emits a bounded diagnostic without failing on an unresolved gap.</summary>
    public static LoseTargetingContentAuditResult Inspect(SqliteConnection connection)
    {
        var result = Read(connection);
        Logger.Info(
            "GF-C08 LoseTargeting content audit: content={0} serverLinks={1} allLinks={2} " +
            "unlinkedServer={3} unlinkedAll={4} sources=[{5}]",
            result.ContentRowCount,
            result.LinkedRowCount(LoseTargetingLinkScope.ServerEffectLinks),
            result.LinkedRowCount(LoseTargetingLinkScope.AllContentLinks),
            result.UnlinkedIds(LoseTargetingLinkScope.ServerEffectLinks).Count,
            result.UnlinkedIds(LoseTargetingLinkScope.AllContentLinks).Count,
            string.Join(",", result.LinkCounts.OrderBy(entry => entry.Key)
                .Select(entry => $"{entry.Key}:{entry.Value}")));
        return result;
    }

    private static void AddServerLinks(
        SqliteConnection connection,
        List<LoseTargetingContentLink> links,
        LoseTargetingLinkSource source,
        string linkTable,
        bool requireEnabled)
    {
        var enabledClause = requireEnabled ? " AND link.enable = 't'" : string.Empty;
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT DISTINCT effect.actual_id FROM {linkTable} AS link " +
            "JOIN effects AS effect ON effect.id = link.effect_id " +
            "WHERE effect.actual_type IN ('SpecialEffect', 'BuffEffect')" + enabledClause +
            " AND effect.actual_id IN (SELECT id FROM special_effects WHERE special_effect_type_id = @type)";
        command.Parameters.AddWithValue("@type", (int)SpecialType.LoseTargetingTheTarget);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
                throw new InvalidDataException($"GF-C08 {linkTable} audit found a null effect.actual_id.");
            links.Add(new LoseTargetingContentLink(Convert.ToUInt32(reader.GetValue(0)), source));
        }
    }
}
