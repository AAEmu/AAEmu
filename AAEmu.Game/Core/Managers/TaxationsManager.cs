using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер загрузки шаблонов налогообложения из таблицы <c>taxations</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class TaxationsManager : Singleton<TaxationsManager>, ITaxationsManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Dictionary<uint, Taxation> taxations;
    public Dictionary<uint, Taxation> Taxations => taxations;

    /// <summary>
    /// Загружает все записи из таблицы <c>taxations</c>.
    /// </summary>
    /// <remarks>
    /// Схема таблицы <c>taxations</c> (проверена по compact.sqlite3):
    /// <list type="bullet">
    ///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Taxation.Id"/></description></item>
    ///   <item><description><c>desc</c> text → <see cref="Taxation.Desc"/></description></item>
    ///   <item><description><c>seal_count</c> int → <see cref="Taxation.SealCount"/></description></item>
    ///   <item><description><c>show</c> bool → <see cref="Taxation.Show"/></description></item>
    ///   <item><description><c>tax</c> int → <see cref="Taxation.Tax"/></description></item>
    /// </list>
    /// </remarks>
    public void Load()
    {
        taxations = [];

        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading taxations ...");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM taxations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Taxation
                        {
                            Id = reader.GetUInt32("id"),
                            Desc = reader.GetString("desc"),
                            SealCount = reader.GetUInt32("seal_count"),
                            Show = reader.GetBoolean("show", true),
                            Tax = reader.GetUInt32("tax")
                        };
                        taxations.Add(template.Id, template);
                    }
                }
            }
        }
    }
}
