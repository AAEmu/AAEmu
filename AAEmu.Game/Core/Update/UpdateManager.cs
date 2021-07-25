using System;
using System.IO;
using System.Collections.Generic;
using System.Data;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;
using System.Linq;

namespace AAEmu.Game.Core.Update
{
    class UpdateManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        internal static string CheckUpdates(string appPath)
        {
            var config = AppConfiguration.Instance.Updates;
            Boolean UpdatesEnabled = config.UpdateSQL;
            string result = "Not Run";
            string NewInstall = "false";
            
            if (UpdatesEnabled == true)
            {
                _log.Info("SQL Updates are enabled, running Updates Manager...");
                string DevBuildPath = Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(Directory.GetParent(appPath).FullName.ToString()).FullName.ToString()).FullName.ToString()).FullName.ToString()).FullName.ToString();
                string EXERootPath = Directory.GetParent(appPath).FullName.ToString();
                _log.Info("Server Path = \"" + appPath + "\"");
                string sqlUpdatePath = "";
                if (Directory.Exists(DevBuildPath + "\\SQL\\Updates") == true)
                {
                    sqlUpdatePath = DevBuildPath + "\\SQL\\Updates";
                    _log.Info("Dev build, update SQL from \"" + sqlUpdatePath + "\\SQL\\Updates\"");
                }
                else
                {
                    sqlUpdatePath = EXERootPath + "\\SQL\\Updates";
                    _log.Info("Release Build, update SQL from \"" + sqlUpdatePath + "\\SQL\\Updates\"");
                }
                // Create Array of all .sql files in the directory
                string[] sqlFilePaths = Directory.GetFiles(sqlUpdatePath, "*.sql");

                // Create an array of available SQL files
                List<FileInfo> sqlFiles = new List<FileInfo>();
                foreach (string sqlFilePath in sqlFilePaths)
                {
                    string sqlFileName = new FileInfo(sqlFilePath).Name.Replace(".sql", "");
                    string applicableServer = sqlFileName.Split("_")[2].ToLower();
                    // Make sure the sql file applies to this server (Relies on YYYY-MM-DD_AAEMU_<Server>_<Description> naming convention)
                    if (applicableServer == "game")
                    {
                        _log.Info(" - \"" + sqlFileName + "\" is applicable to this server and will be checked!");
                        sqlFiles.Add(new FileInfo(sqlFilePath));
                    } else
                    {
                        _log.Info(" - \"" + sqlFileName + "\" is for server type \"" + applicableServer + "\", skipping.");
                    }
                }
                if (sqlFiles.Count > 0)
                {
                    _log.Info("There are " + sqlFiles.Count.ToString() + " SQL files to process.");
                    // Read Value from mySQL table (NewInstall = True/False), base actions on this
                    using (var connect = MySQL.CreateConnection())
                    {
                        using (var command = connect.CreateCommand())
                        {
                            command.CommandText = "SELECT * FROM server_properites WHERE PropertyName = 'NewInstall'";
                            command.Prepare();
                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    NewInstall = reader.GetString("PropertyValue").ToLower();
                                    connect.Close();
                                    connect.Open();
                                    switch (NewInstall)
                                    {
                                        case "true":
                                            _log.Info("This is a NEW INSTALL; recording all existing SQL files with state as 'applied' with no execution...");
                                            bool AllSQLRecordSuccess = true;
                                            foreach (FileInfo sqlFile in sqlFiles)
                                            {
                                                // create an entry for each SQL file found, and change the NewInstall property value to FALSE
                                                string sqlFileNameStripped = sqlFile.Name.Replace(".sql", "");
                                                command.CommandText = "INSERT INTO `server_db_updates` (`UpdateName`, `UpdateDescription`) VALUES ('" + sqlFileNameStripped + "', 'INITIAL INSTALLATION')";
                                                command.Prepare();
                                                if (command.ExecuteNonQuery() == 1)
                                                {
                                                    _log.Info("- " + sqlFileNameStripped + ": Record Success");
                                                } else
                                                {
                                                    _log.Error("- " + sqlFileNameStripped + ": Record FAILED");
                                                    AllSQLRecordSuccess = false;
                                                }
                                            }
                                            if (AllSQLRecordSuccess == true)
                                            {
                                                _log.Info("- All SQL files recoreded successfully, setting NewInstall flag to false.");
                                                command.CommandText = "UPDATE `server_properites` SET `PropertyValue` = 'False' WHERE (`PropertyName` = 'NewInstall')";
                                                command.Prepare();
                                                if (command.ExecuteNonQuery() == 1)
                                                {
                                                    _log.Info("- NewInstall flag succesfully changed.");
                                                    result = "Success - New Installation Configured";
                                                } else
                                                {
                                                    _log.Error("- Error changing NewInstall flag.");
                                                    result = "Error - Error Changing NewInstall Flag";
                                                }
                                            } else
                                            {
                                                _log.Error("- Error recording one or more SQL updates.");
                                                result = "Error - Error recording one or more SQL updates";
                                            }
                                            break;
                                        default:
                                            _log.Info("This is an EXISTING install; any sql file found that has not already been recorded will be executed...");
                                            // Create a list of installed SQL updates recorded in the DB
                                            command.CommandText = "SELECT * FROM server_db_updates";
                                            command.Prepare();
                                            var installedSQLDataTable = new DataTable();
                                            installedSQLDataTable.Load(command.ExecuteReader());
                                            var recordedSQLUpdates = installedSQLDataTable.AsEnumerable().ToArray();
                                            List<string> InstalledsqlFiles = new List<string>();
                                            foreach (var recordedSQLUpdate in recordedSQLUpdates)
                                            {
                                                InstalledsqlFiles.Add(recordedSQLUpdate["UpdateName"].ToString());
                                            }

                                            // Check to see if each locally available SQL file is already installed, run it if not
                                            bool AllSQLInstallSuccess = true;
                                            foreach (FileInfo sqlFile in sqlFiles)
                                            {
                                                string sqlFileNameStripped = sqlFile.Name.Replace(".sql", "");
                                                if (InstalledsqlFiles.Contains(sqlFileNameStripped))
                                                {
                                                    _log.Info(sqlFileNameStripped + " - ALREADY INSTALLED");
                                                } else
                                                {
                                                    _log.Info(sqlFileNameStripped + " - NOT INSTALLED");
                                                    // TODO: Run the sql files
                                                    connect.Close();
                                                    connect.Open();
                                                    MySql.Data.MySqlClient.MySqlScript dbUpdateScript = new MySql.Data.MySqlClient.MySqlScript(connect, File.ReadAllText(sqlFile.FullName));
                                                    // dbUpdateScript.Delimiter = "$$";
                                                    if (dbUpdateScript.Execute() > 0)
                                                    {
                                                        _log.Info("- " + sqlFileNameStripped + ": Execute Update SUCCESS");
                                                        connect.Close();
                                                        connect.Open();
                                                        command.CommandText = "INSERT INTO `server_db_updates` (`UpdateName`, `UpdateDescription`) VALUES ('" + sqlFileNameStripped + "', 'Description not yet implemented')";
                                                        command.Prepare();
                                                        if (command.ExecuteNonQuery() == 1)
                                                        {
                                                            _log.Info("- " + sqlFileNameStripped + ": Record Success");
                                                        }
                                                        else
                                                        {
                                                            _log.Error("- " + sqlFileNameStripped + ": Record FAILED");
                                                        }
                                                    } else
                                                    {
                                                        _log.Error("- " + sqlFileNameStripped + ": Execute Update FAILED");
                                                        AllSQLInstallSuccess = false;
                                                    }
                                                }
                                            }
                                            if (AllSQLInstallSuccess == true)
                                            {
                                                _log.Info("- All Updates Installed Successfully.");
                                                result = "Success - All Updates Installed or Checked Successfully";
                                            } else
                                            {
                                                _log.Error("- Error installing one or more SQL updates.");
                                                result = "Error - Error installing one or more SQL updates";
                                            }
                                            break;
                                    }
                                }
                                else
                                {
                                    _log.Info("SQL - Failure");
                                }
                            }
                        }
                    }
                } else
                {
                    _log.Info("There were no SQL files to process, Skipping.");
                    result = "Success - No Updates to Process";
                }
            }
            else
            {
                _log.Info("SQL Updates are NOT enabled.  Skipping Updates Manager.");
                result = "Success - Skipped";
            }
            return result;
        }
    }
}
