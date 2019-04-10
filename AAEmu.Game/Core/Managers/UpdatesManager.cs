// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see licence.txt in the main folder

using System;
using System.IO;
using System.Text.RegularExpressions;
using AAEmu.Commons.Database;
using AAEmu.Commons.Utils;
using AAEmu.Game.Utils.DB;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class UpdatesManager : Singleton<UpdatesManager>
    {

        private static Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Checks whether the SQL update file has already been applied.
        /// </summary>
        /// <param name="updateFile"></param>
        /// <returns></returns>
        public bool CheckUpdate(string updateFile)
        {
            using (var connection = MySQL.CreateConnection())
            {
                using (var mc = new MySqlCommand("SELECT * FROM `updates` WHERE `path` = @path", connection))
                {
                    mc.Parameters.AddWithValue("@path", updateFile);

                    using (var reader = mc.ExecuteReader())
                    {
                        return reader.Read();
                    }
                }
            }
        }

        /// <summary>
        /// Executes SQL update file.
        /// </summary>
        /// <param name="updateFile"></param>
        public void RunUpdate(string updateFile)
        {

            string[] result = Regex.Split(File.ReadAllText(Path.Combine("sql", updateFile)), @"([^\\];)");
            try
            {
                using (var connection = MySQL.CreateConnection())
                {

                    //stop autocommit
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SET AUTOCOMMIT = 0";
                        cmd.ExecuteNonQuery();
                    }
                    //Run update
                    using (var cmd = new MySqlCommand(File.ReadAllText(Path.Combine("sql", updateFile)), connection))
                    {
                        //_log.Info("GameServer need to connect only after loading all SQL! We are waiting for a long download of large SQL files!");
                        cmd.CommandTimeout = 120; //3600; //ждем долгой загрузки больших SQL файлов. Обычно, это значение - десятки секунд.
                        cmd.ExecuteNonQuery();
                    }

                    // Log update
                    using (var cmd = new InsertCommand("INSERT INTO `updates` {0}", connection))
                    {
                        cmd.Set("path", updateFile);
                        cmd.Execute();
                    }

                    // recovery autocommit
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SET AUTOCOMMIT = 1";
                        cmd.ExecuteNonQuery();
                    }

                    _log.Info("Successfully applied '{0}'.", updateFile);
                }
            }
            catch (Exception ex)
            {
                using (var connection = MySQL.CreateConnection())
                {
                    MySqlTransaction transaction = connection.BeginTransaction();

                    // recovery autocommit
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SET AUTOCOMMIT = 1";
                        cmd.ExecuteNonQuery();
                    }
                }

                _log.Error("RunUpdate: Failed to run '{0}': {1}", updateFile, ex.Message);
                CliUtil.Exit(1);
            }
        }
    }
}
