using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.AI.Params;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class AiGameData : Singleton<AiGameData>, IGameDataLoader
    {
        public AlmightNpcParams ANTHALON { get; set; }
        public void Load(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_ai_params WHERE id = 1242";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        ANTHALON = new AlmightNpcParams();
                        ANTHALON.Parse(reader.GetString("ai_param"));
                    }
                }
            }
        }

        public void PostLoad()
        {
        }
    }
}
