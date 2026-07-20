using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Emotion;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер загрузки текстов эмоций/выражений из таблицы <c>express_texts</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ExpressTextManager : Singleton<ExpressTextManager>, IExpressTextManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, uint> _expressTexts;

    public uint GetExpressAnimId(uint emotionId)
    {
        return _expressTexts.TryGetValue(emotionId, out var text) ? text : 0;
    }

    /// <summary>
    /// Загружает все записи из таблицы <c>express_texts</c>.
    /// </summary>
    /// <remarks>
    /// Схема таблицы <c>express_texts</c> (проверена по compact.sqlite3):
    /// <list type="bullet">
    ///   <item><description><c>id</c> int PRIMARY KEY → <see cref="ExpressText.Id"/></description></item>
    ///   <item><description><c>anim_id</c> int → <see cref="ExpressText.AnimId"/></description></item>
    ///   <item><description><c>me</c> text → <see cref="ExpressText.Me"/></description></item>
    ///   <item><description><c>me_target</c> text → <see cref="ExpressText.MeTarget"/></description></item>
    ///   <item><description><c>npc_anim_id</c> int → <see cref="ExpressText.NpcAnimId"/></description></item>
    ///   <item><description><c>other</c> text → <see cref="ExpressText.Other"/></description></item>
    ///   <item><description><c>other_me</c> text → <see cref="ExpressText.OtherMe"/></description></item>
    ///   <item><description><c>other_target</c> text → <see cref="ExpressText.OtherTarget"/></description></item>
    /// </list>
    /// </remarks>
    public void Load()
    {
        _expressTexts = [];

        Logger.Info("Loading express text...");

        using var connection = SQLite.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM express_texts";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new ExpressText
             {
                 Id = reader.GetUInt32("id"),
                 AnimId = reader.GetUInt32("anim_id"),
                 Me = reader.GetString("me", null),
                 MeTarget = reader.GetString("me_target", null),
                 NpcAnimId = reader.GetUInt32("npc_anim_id"),
                 Other = reader.GetString("other", null),
                 OtherMe = reader.GetString("other_me", null),
                 OtherTarget = reader.GetString("other_target", null)
             };

            if (!_expressTexts.ContainsKey(template.Id))
            {
                _expressTexts.Add(template.Id, template.AnimId);
            }
        }
    }
}
