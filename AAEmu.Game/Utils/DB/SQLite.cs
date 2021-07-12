using System;
using System.IO;
using AAEmu.Commons.IO;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Utils.DB
{
    public static class SQLite
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public static SqliteConnection CreateConnection()
        {
            var dbPath = Path.Combine(FileManager.AppPath, "Data", "compact.sqlite3");
            if (!File.Exists(dbPath))
            {
                _log.Fatal("Server database does not exist: {0} !",dbPath);
                return null;
            }
            var connection = new SqliteConnection($"Data Source=file:{dbPath}; Mode=ReadOnly");
            try
            {
                connection.Open();
            }
            catch (Exception e)
            {
                _log.Error(e,"Error on SQLite connect: {0}", e.Message);
                return null;
            }

            return connection;
        }
    }
}
