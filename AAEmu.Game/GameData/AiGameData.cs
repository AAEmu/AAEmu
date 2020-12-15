using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.AI.Params;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class AiGameData : Singleton<AiGameData>, IGameDataLoader
    {
        private Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, AiParams> _aiParams;

        public AiParams GetAiParamsForId(uint id)
        {
            if (_aiParams.ContainsKey(id))
                return _aiParams[id];
            return null;
        }
        
        public void Load(SqliteConnection connection)
        {
            _aiParams = new Dictionary<uint, AiParams>();
            
            var fileTypeToId = new Dictionary<uint, AiParamType>();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT ai_file_id, npc_ai_param_id FROM npcs";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var type = (AiParamType)reader.GetUInt32("ai_file_id");
                        var id = reader.GetUInt32("npc_ai_param_id");
                        if (!fileTypeToId.ContainsKey(id))
                            fileTypeToId.Add(id, type);
                    }
                }
            }
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_ai_params";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        if (!fileTypeToId.ContainsKey(id))
                            continue;

                        var fileType = fileTypeToId[id];
                        
                        var aiParams = AiParams.GetByType(fileType);
                        if (aiParams == null)
                            continue; // TODO : unimplemented

                        var data = reader.GetString("ai_param");
                        try
                        {
                            aiParams.Parse(data);
                            if (!_aiParams.ContainsKey(id))
                                _aiParams.Add(id, aiParams);
                        }
                        catch (Exception e)
                        {
                            _log.Warn("Impossible to parse npc_ai_params {0}", id);
                        }
                    }
                }
            }
        }

        public void PostLoad()
        {
        }
    }
}
