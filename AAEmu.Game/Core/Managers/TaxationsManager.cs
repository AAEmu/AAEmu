using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Taxations;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class TaxationsManager : Singleton<TaxationsManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Dictionary<uint, Taxation> taxations;

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
                        var template = new Taxation();
                        template.Id = reader.GetUInt32("id");
                        template.Tax = reader.GetUInt32("tax");
                        template.Show = reader.GetBoolean("show", true);
                        taxations.Add(template.Id, template);
                    }
                }
            }
        }
    }
}
