using System;
using System.Collections.Generic;
using System.IO;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Json;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class TeleportReturnPointGameData : Singleton<TeleportReturnPointGameData>, IGameDataLoader
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, JsonDoodadSpawns> _teleportReturnPoint;

        public JsonDoodadSpawns GetTeleportReturnPoint(uint id)
        {
            if (_teleportReturnPoint.ContainsKey(id))
                return _teleportReturnPoint[id];
            return null;
        }

        public void Load(SqliteConnection connection)
        {
            _teleportReturnPoint = new Dictionary<uint, JsonDoodadSpawns>();

            _log.Info("Loading Teleport Return Point...");
            var teleportReturnPoints = new List<JsonDoodadSpawns>();
            var contents = string.Empty;
            var worldPath = Path.Combine(FileManager.AppPath, "Data");
            var jsonFileName = Path.Combine(worldPath, "teleport_return_points.json");

            if (!File.Exists(jsonFileName))
            {
                _log.Info("File is missing {0}", Path.GetFileName(jsonFileName));
            }
            else
            {
                contents = FileManager.GetFileContents(jsonFileName);
                if (string.IsNullOrWhiteSpace(contents))
                    _log.Warn("File {0} is empty.", jsonFileName);
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out teleportReturnPoints, out _))
                    {
                        foreach (var teleportReturnPoint in teleportReturnPoints)
                        {
                            _teleportReturnPoint.Add(teleportReturnPoint.Id, teleportReturnPoint);
                        }
                    }
                    else
                        throw new Exception(string.Format("TeleportReturnPointGameData: Parse {0} file", jsonFileName));
                }
            }
        }

        public void PostLoad()
        {
        }
    }
}
