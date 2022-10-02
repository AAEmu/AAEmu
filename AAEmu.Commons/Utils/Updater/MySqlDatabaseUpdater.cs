using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AAEmu.Commons.IO;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Commons.Utils.Updater
{
    public static class MySqlDatabaseUpdater
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        /*
            CREATE TABLE `updates` (
              `script_name` varchar(255) NOT NULL,
              `installed` tinyint NOT NULL DEFAULT '0',
              `install_date` datetime NOT NULL,
              `last_error` text NOT NULL
            ) COLLATE 'utf8mb4_general_ci';
        */

        /// <summary>
        /// Checks if the updates table already exists
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="databaseSchemaName"></param>
        /// <returns></returns>
        private static bool UpdatesTableExists(MySqlConnection connection, string databaseSchemaName)
        {
            var updateDbExists = false;
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;

                // Check if the updates table exists or not
                command.CommandText = "SELECT EXISTS( SELECT `TABLE_NAME` FROM `INFORMATION_SCHEMA`.`TABLES` WHERE (`TABLE_NAME` = 'updates') AND (`TABLE_SCHEMA` = @db_name) ) as `is-exists`;";
                command.Parameters.AddWithValue("@db_name", databaseSchemaName);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read() && (reader.HasRows) && (reader.GetInt32("is-exists") > 0))
                        updateDbExists = true;
                }
            }
            return updateDbExists;
        }
        
        /// <summary>
        /// Create initial table
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private static bool CreateUpdatesTable(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE `updates` ( " +
                                      "`script_name` varchar(255) NOT NULL, " +
                                      "`installed` tinyint NOT NULL DEFAULT '0', " +
                                      "`install_date` datetime NOT NULL, " +
                                      "`last_error` text NOT NULL " +
                                      ") COMMENT='Table containing SQL update script information' COLLATE 'utf8mb4_general_ci';" +
                                      "ALTER TABLE `updates` ADD PRIMARY KEY `script_name` (`script_name`);";
                try
                {
                    command.ExecuteNonQuery();
                }
                catch
                {
                    _log.Fatal("Failed to create updates table!");
                    // Failed to create the new table
                    return false;
                }

                _log.Info("Created updates table");
            }
            return true;
        }

        /// <summary>
        /// Mark all current updates as installed
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="allUpdatesFiles"></param>
        /// <param name="moduleNamePrefix"></param>
        /// <returns></returns>
        private static void InitializeUpdatesTable(MySqlConnection connection, List<string> allUpdatesFiles, string moduleNamePrefix)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;

                foreach (var thisScriptFile in allUpdatesFiles)
                {
                    var fName = Path.GetFileName(thisScriptFile);
                    if (fName == null)
                        continue; // shouldn't happen here
                    fName = fName.ToLower();
                    if (!fName.Contains(moduleNamePrefix))
                        continue; // These files are not related to us, ignore (technically shouldn't happen, but add it anyway)

                    command.CommandText = "REPLACE INTO `updates` " +
                                          "(`script_name`,`installed`,`install_date`,`last_error`" +
                                          ") VALUES (" +
                                          "@script_name,@installed,@install_date,@last_error)";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@script_name", fName);
                    command.Parameters.AddWithValue("@installed", 1);
                    command.Parameters.AddWithValue("@install_date", DateTime.UtcNow);
                    command.Parameters.AddWithValue("@last_error", "Initialized");

                    command.ExecuteNonQuery();
                }
            }
        }

        private static bool InstallUpdatesFiles(MySqlConnection connection, List<string> filesToRun, bool doSkip)
        {
            foreach (var fName in filesToRun)
            {
                var success = false;
                var errorText = string.Empty;
                if (doSkip == false)
                {
                    var sql = File.ReadAllText(fName);

                    // Run update script
                    success = false;
                    errorText = string.Empty;
                    using (var command = connection.CreateCommand())
                    {
                        command.Connection = connection;
                        command.CommandText = sql;

                        try
                        {
                            command.ExecuteNonQuery();
                            success = true;
                        }
                        catch (Exception e)
                        {
                            errorText = e.Message;
                        }
                    }
                }
                else
                {
                    success = true;
                    errorText = "Skipped";
                }

                // Save the results
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.CommandText = "REPLACE INTO `updates` " +
                                          "(`script_name`,`installed`,`install_date`,`last_error`" +
                                          ") VALUES (" +
                                          "@script_name,@installed,@install_date,@last_error)";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@script_name", Path.GetFileName(fName));
                    command.Parameters.AddWithValue("@installed", success ? 1 : 0);
                    command.Parameters.AddWithValue("@install_date", success ? DateTime.UtcNow : DateTime.MinValue);
                    command.Parameters.AddWithValue("@last_error", errorText);

                    command.ExecuteNonQuery();
                }

                if (!success)
                {
                    _log.Error($"Failed to run update script: {fName}");
                    _log.Error(errorText);
                    return false;
                }

                _log.Info(doSkip ? $"Skipped: {fName}" : $"Installed: {fName}");
                //filesAlreadyUpdated.Add(fName);
            }

            return true;
        }

        /// <summary>
        /// Scans and runs updates from the SQL\Updates folder
        /// </summary>
        /// <param name="connection">A valid MySqlConnection</param>
        /// <param name="moduleNamePrefix">either aaemu_login or aaemu_game</param>
        /// <param name="databaseSchemaName">actual database name for this configuration</param>
        /// <returns></returns>
        public static bool Run(MySqlConnection connection, string moduleNamePrefix, string databaseSchemaName)
        {
            _log.Debug($"Updating database for {moduleNamePrefix}");

            // Check if the updates table already exists
            var updateDbExists = UpdatesTableExists(connection, databaseSchemaName);

            // (try to) Create the table if it doesn't exist yet
            if (updateDbExists == false)
            {
                if (!CreateUpdatesTable(connection))
                {
                    _log.Fatal($"Was unable to create updates table in {databaseSchemaName} !");
                    return false;
                }
            }

            // Get the Updates Files List
            var updatesFolder = FindUpdatesFolder(moduleNamePrefix, out var allUpdatesFiles);
            allUpdatesFiles.Sort();
            var filesToRun = new List<string>();
            var filesAlreadyUpdated = new List<string>();

            if (string.IsNullOrWhiteSpace(updatesFolder) || (allUpdatesFiles.Count <= 0))
            {
                _log.Info("No sql update folder or files found.");
                return true;
            }

            // If we're running this version for the first time, assume that all updates have been installed before
            if (updateDbExists == false)
                InitializeUpdatesTable(connection,allUpdatesFiles,moduleNamePrefix);

            // Load the DB contents
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;

                // Query the DB for entries marked as not installed yet
                command.CommandText = "SELECT * FROM `updates` ORDER BY `script_name`";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var fName = reader.GetString("script_name");
                        var installed = reader.GetInt32("installed");

                        var fullPathName = Path.Combine(updatesFolder, fName);
                        if (File.Exists(fullPathName))
                        {
                            if (installed <= 0)
                                filesToRun.Add(fullPathName);
                            else
                                filesAlreadyUpdated.Add(fullPathName);
                        }
                    }
                }
            }

            // Add remaining files to lists that are not in the DB yet
            foreach (var fName in allUpdatesFiles)
            {
                if (filesToRun.Contains(fName))
                    continue;
                if (filesAlreadyUpdated.Contains(fName))
                    continue;
                if (File.Exists(fName))
                    filesToRun.Add(fName);
            }

            if (filesToRun.Count > 0)
            {
                Thread.Sleep(1000);
                Console.WriteLine($"Warning, there are {filesToRun.Count} updates for the database that need to be installed first!");
                Console.WriteLine("-----");
                foreach (var fName in filesToRun)
                {
                    Console.WriteLine($"> {fName}");
                }

                Console.WriteLine("-----");
                Console.Write("Please type YES (all caps) to try and automatically install the updates, type SKIP if you already installed the update manually, or press Ctrl+C here to quit: ");
                var yesNo = Console.ReadLine();
                if ((yesNo != "YES") && (yesNo != "SKIP"))
                    return false;

                if (!InstallUpdatesFiles(connection, filesToRun, yesNo == "SKIP"))
                    return false;
            }
            else
            {
                _log.Debug("No DB update required");
            }

            return true;
        }

        private static string FindUpdatesFolder(string moduleNamePrefix, out List<string> res)
        {
            // Crawl up to root directory to find a good folder
            var currentDir = FileManager.AppPath;
            while (currentDir.Split(Path.DirectorySeparatorChar).Length > 1)
            {
                var testDir = Path.Combine(currentDir, "SQL", "updates");
                if (Directory.Exists(testDir))
                {
                    var tryFiles = Directory
                        .GetFiles(testDir, "*" + moduleNamePrefix + "*.sql", SearchOption.TopDirectoryOnly).ToList();
                    if (tryFiles.Count > 0)
                    {
                        res = tryFiles;
                        return testDir;
                    }
                }

                try
                {
                    currentDir = Directory.GetParent(currentDir)?.FullName ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                    currentDir = string.Empty;
                }
            }

            res = new List<string>();
            return string.Empty;
        }
    }
}
