using System;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Utils.DB
{
    public static class SQLite
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public static SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection("Data Source=file:Data/compact.sqlite3; Mode=ReadOnly");
            try
            {
                connection.Open();
            }
            catch (Exception e)
            {
                _log.Error("Error on SQLite connect: {0}", e.Message);
                return null;
            }

            return connection;
        }
    }
}