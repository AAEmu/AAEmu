using AAEmu.Commons.Utils.DB;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Named sentinels for <c>SCSurveyFormSave.result</c>. The client's numeric result mapping was
/// not recovered from the shipped client, so only the names are pinned here.
/// </summary>
public enum SurveyFormReplyResult : byte
{
    Success = 0,
    UnknownForm = 1,
    NotOpen = 2,
    AlreadyReplied = 3,
}

public sealed record SurveyFormReplyOutcome(uint FormId, SurveyFormReplyResult Result)
{
    public bool Success => Result == SurveyFormReplyResult.Success;

    public ErrorMessageType Error => Result switch
    {
        SurveyFormReplyResult.Success => ErrorMessageType.NoErrorMessage,
        SurveyFormReplyResult.AlreadyReplied => ErrorMessageType.SurveyFormAlreadyDone,
        SurveyFormReplyResult.NotOpen => ErrorMessageType.SurveyFormInvalidPeriod,
        _ => ErrorMessageType.SurveyFormInvalidCommon
    };
}

/// <summary>Per-account survey replies (<c>account_survey_form_replies</c>).</summary>
public interface ISurveyFormReplyStore
{
    bool HasReplied(ulong accountId, uint surveyFormId);

    /// <summary>False when the unique (account, form) row already exists — the exactly-once guard.</summary>
    bool TryRecord(ulong accountId, uint surveyFormId, uint characterId, DateTime repliedAt);
}

public sealed class MySqlSurveyFormReplyStore : ISurveyFormReplyStore
{
    private const int DuplicateKeyErrorNumber = 1062;

    private readonly Func<MySqlConnection> _connectionFactory;

    public MySqlSurveyFormReplyStore() : this(MySQL.CreateConnection)
    {
    }

    internal MySqlSurveyFormReplyStore(Func<MySqlConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public bool HasReplied(ulong accountId, uint surveyFormId)
    {
        using var connection = _connectionFactory();
        using var connectionCommand = connection.CreateCommand();
        connectionCommand.CommandText =
            "SELECT COUNT(*) FROM account_survey_form_replies " +
            "WHERE account_id = @account_id AND survey_form_id = @survey_form_id";
        connectionCommand.Parameters.AddWithValue("@account_id", accountId);
        connectionCommand.Parameters.AddWithValue("@survey_form_id", surveyFormId);
        return Convert.ToInt64(connectionCommand.ExecuteScalar()) > 0;
    }

    public bool TryRecord(ulong accountId, uint surveyFormId, uint characterId, DateTime repliedAt)
    {
        using var connection = _connectionFactory();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO account_survey_form_replies " +
            "(account_id, survey_form_id, character_id, replied_at) " +
            "VALUES (@account_id, @survey_form_id, @character_id, @replied_at)";
        command.Parameters.AddWithValue("@account_id", accountId);
        command.Parameters.AddWithValue("@survey_form_id", surveyFormId);
        command.Parameters.AddWithValue("@character_id", characterId);
        command.Parameters.AddWithValue("@replied_at", ToUnixSeconds(repliedAt));
        try
        {
            return command.ExecuteNonQuery() == 1;
        }
        catch (MySqlException ex) when (ex.Number == DuplicateKeyErrorNumber)
        {
            return false;
        }
    }

    private static long ToUnixSeconds(DateTime moment) =>
        new DateTimeOffset(ServerCalendar.AsUtc(moment)).ToUnixTimeSeconds();
}

/// <summary>
/// Survey-form replies: the form must exist in shipped content and be inside its window, and an
/// account may reply exactly once (the shipped survey_forms text pins the reward to one reply per
/// account). Every refusal is a definitive result, never silence.
/// </summary>
public sealed class SurveyFormService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ISurveyFormReplyStore _store;

    public SurveyFormService(ISurveyFormReplyStore store)
    {
        _store = store;
    }

    public static SurveyFormService Instance { get; private set; } = new(new MySqlSurveyFormReplyStore());

    public static void SetInstanceForTest(SurveyFormService service) =>
        Instance = service ?? new SurveyFormService(new MySqlSurveyFormReplyStore());

    /// <param name="forceFuture">Client flag as sent; its meaning was not recovered, so it is accepted and ignored.</param>
    public SurveyFormReplyOutcome Reply(
        ulong accountId, uint formId, uint characterId, DateTime now, byte forceFuture)
    {
        _ = forceFuture;

        if (!SurveyFormGameData.Instance.TryGet(formId, out var form))
        {
            Logger.Warn("Survey reply: survey_forms row {0} does not exist.", formId);
            return new SurveyFormReplyOutcome(formId, SurveyFormReplyResult.UnknownForm);
        }

        var moment = ServerCalendar.AsUtc(now);
        var start = ServerCalendar.AsUtc(form.Start);
        var end = ServerCalendar.AsUtc(form.End);
        if (moment < start || moment > end)
        {
            Logger.Warn("Survey reply: form {0} is not open at {1:u} (window {2:u} .. {3:u}).",
                formId, moment, start, end);
            return new SurveyFormReplyOutcome(formId, SurveyFormReplyResult.NotOpen);
        }

        if (_store.HasReplied(accountId, formId))
            return new SurveyFormReplyOutcome(formId, SurveyFormReplyResult.AlreadyReplied);

        if (!_store.TryRecord(accountId, formId, characterId, moment))
            return new SurveyFormReplyOutcome(formId, SurveyFormReplyResult.AlreadyReplied);

        return new SurveyFormReplyOutcome(formId, SurveyFormReplyResult.Success);
    }
}
