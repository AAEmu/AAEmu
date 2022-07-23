using System;
using AAEmu.Commons.Models;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Commons.Utils.DB
{
    public static class MySQL
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static string s_connectionString;

        static MySQL()
        {
            SetConfiguration(null);
        }

        public static MySqlConnection CreateConnection()
        {
            var connection = new MySqlConnection(s_connectionString);
            try
            {
                connection.Open();
            }
            catch (Exception e)
            {
                _log.Fatal($"Error on DB connect: {e.Message}");
                return null;
            }

            return connection;
        }

        public static void Close(MySqlConnection connection)
        {
            connection.Close();
        }
        
        public static void SetConfiguration(MySqlConnectionSettings mySqlConnectionSettings)
        {
            var builder = new MySqlConnectionStringBuilder()
            {
                Server = mySqlConnectionSettings?.Host ?? "localhost",
                Port = mySqlConnectionSettings?.Port ?? 3306,
                UserID = mySqlConnectionSettings?.User ?? "root",
                Password = mySqlConnectionSettings?.Password ?? "",
                Database = mySqlConnectionSettings?.Database ?? "",
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 10,
                ConnectionLifeTime = 600,
                CharacterSet = "utf8",
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true,
                DefaultCommandTimeout = 180,
                SslMode = MySqlSslMode.Prefered
            };
            s_connectionString = builder.ConnectionString;
        }
    }
}
