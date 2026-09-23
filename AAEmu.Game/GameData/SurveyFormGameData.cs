using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// One publisher survey form: <c>survey_forms</c> plus its reply window (the shipped st_/ed_
/// date-time columns). A reply outside that window, or for an id that is not in this table, is
/// refused — there is no fallback row.
/// </summary>
public sealed record SurveyFormInfo(uint Id, DateTime Start, DateTime End);

/// <summary><c>survey_forms</c> reply windows, keyed by id.</summary>
[GameData]
public class SurveyFormGameData : Singleton<SurveyFormGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, SurveyFormInfo> _forms = new();

    public IReadOnlyCollection<SurveyFormInfo> Forms => _forms.Values;

    public void Load(SqliteConnection connection)
    {
        var forms = new Dictionary<uint, SurveyFormInfo>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   st_year, st_month, st_day, st_hour, st_min,
                   ed_year, ed_month, ed_day, ed_hour, ed_min
            FROM survey_forms
            ORDER BY id
            """;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var id = reader.GetUInt32("id");
            try
            {
                var start = new DateTime(
                    reader.GetInt32("st_year"), reader.GetInt32("st_month"), reader.GetInt32("st_day"),
                    reader.GetInt32("st_hour"), reader.GetInt32("st_min"), 0, DateTimeKind.Unspecified);
                var end = new DateTime(
                    reader.GetInt32("ed_year"), reader.GetInt32("ed_month"), reader.GetInt32("ed_day"),
                    reader.GetInt32("ed_hour"), reader.GetInt32("ed_min"), 0, DateTimeKind.Unspecified);
                forms[id] = new SurveyFormInfo(id, start, end);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // Loud skip, never a synthesized window: the row is unusable and replies to it
                // will be refused as unknown.
                Logger.Error(ex, "survey_forms row {0} has an unusable reply window; skipping it.", id);
            }
        }

        _forms = forms;
        Logger.Info("Loaded {0} survey form reply window(s)", _forms.Count);
    }

    public void PostLoad()
    {
    }

    public bool TryGet(uint id, out SurveyFormInfo form) => _forms.TryGetValue(id, out form);

    /// <summary>Replaces the table for tests that cannot open compact.</summary>
    public void SetForTest(IReadOnlyList<SurveyFormInfo> forms)
    {
        _forms = forms == null
            ? new Dictionary<uint, SurveyFormInfo>()
            : forms.ToDictionary(form => form.Id);
    }
}
