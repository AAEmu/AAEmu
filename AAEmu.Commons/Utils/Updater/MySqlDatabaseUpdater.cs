using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AAEmu.Commons.IO;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Commons.Utils.Updater;

/// <summary>
/// 提供用于自动执行 MySQL 数据库更新脚本的功能。
/// 此类会跟踪已应用的更新，并按顺序运行新的更新脚本。
/// </summary>
public static class MySqlDatabaseUpdater
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    /*
         更新表结构 (updates table structure):
        CREATE TABLE `updates` (
          `script_name` varchar(255) NOT NULL,
          `installed` tinyint NOT NULL DEFAULT '0',
          `install_date` datetime NOT NULL,
          `last_error` text NOT NULL
        ) COLLATE 'utf8mb4_general_ci';
    */

    /// <summary>
    /// 检查更新表是否已存在
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

            // 检查更新表是否存在
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
    /// 创建初始表
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
            catch (MySqlException ex)
            {
                Logger.Fatal(ex, "Failed to create updates table!");
                // 创建新表失败
                return false;
            }

            Logger.Info("Created updates table");
        }
        return true;
    }

    /// <summary>
    /// 将所有当前更新标记为已安装
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
                    continue; // 这里不应该发生
                fName = fName.ToLower();
                if (!fName.Contains(moduleNamePrefix))
                    continue; // 这些文件与我们无关，忽略（理论上不应该发生，但还是加上）

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

    /// <summary>
    /// 安装或跳过指定的数据库更新脚本文件。
    /// </summary>
    /// <param name="connection">有效的 MySQL 连接。</param>
    /// <param name="filesToRun">要运行的 SQL 脚本文件的完整路径列表。</param>
    /// <param name="doSkip">如果为 true，则将脚本标记为已安装但跳过实际执行；否则，执行脚本。</param>
    /// <returns>如果所有脚本都成功（执行或跳过），则为 true；否则为 false。</returns>
    private static bool InstallUpdatesFiles(MySqlConnection connection, List<string> filesToRun, bool doSkip)
    {
        foreach (var fName in filesToRun) // 遍历需要处理的每个脚本文件
        {
            var success = false;
            var errorText = string.Empty;
            if (doSkip == false) // 如果不是跳过模式，则执行脚本
            {
                var sql = File.ReadAllText(fName); // 读取 SQL 脚本内容

                // 运行更新脚本
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

            // 保存结果
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
                Logger.Error($"Failed to run update script: {fName}");
                Logger.Error(errorText);
                return false;
            }

            Logger.Info(doSkip ? $"Skipped: {fName}" : $"Installed: {fName}");
            //filesAlreadyUpdated.Add(fName);
        }

        return true;
    }

    /// <summary>
    /// 从 SQL\Updates 文件夹扫描并运行更新
    /// </summary>
    /// <param name="connection">一个有效的 MySqlConnection</param>
    /// <param name="moduleNamePrefix">aaemu_login 或 aaemu_game</param>
    /// <param name="databaseSchemaName">此配置的实际数据库名称</param>
    /// <returns></returns>
    public static bool Run(MySqlConnection connection, string moduleNamePrefix, string databaseSchemaName)
    {
        Logger.Debug($"Updating database for {moduleNamePrefix}");

        // 检查更新表是否已存在
        var updateDbExists = UpdatesTableExists(connection, databaseSchemaName);

        // （尝试）如果表尚不存在则创建表
        if (updateDbExists == false)
        {
            if (!CreateUpdatesTable(connection))
            {
                Logger.Fatal($"Was unable to create updates table in {databaseSchemaName} !");
                return false;
            }
        }

        // 获取更新文件列表
        var updatesFolder = FindUpdatesFolder(moduleNamePrefix, out var allUpdatesFiles);
        allUpdatesFiles.Sort();
        var filesToRun = new List<string>();
        var filesAlreadyUpdated = new List<string>();

        if (string.IsNullOrWhiteSpace(updatesFolder) || (allUpdatesFiles.Count <= 0))
        {
            Logger.Info("No sql update folder or files found.");
            return true;
        }

        // 如果是第一次运行此版本，则假定之前已安装所有更新
        if (updateDbExists == false)
            InitializeUpdatesTable(connection, allUpdatesFiles, moduleNamePrefix);

        // 加载数据库内容
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;

            // 查询数据库中标记为尚未安装的条目
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

        // 将尚未在数据库中的剩余文件添加到列表中
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
            Logger.Debug("No DB update required");
        }

        return true;
    }

    /// <summary>
    /// 查找包含特定模块更新脚本的文件夹。
    /// 它从应用程序路径开始向上查找 "SQL/updates" 子目录，
    /// 并在该目录中搜索与模块前缀匹配的 .sql 文件。
    /// </summary>
    /// <param name="moduleNamePrefix">用于过滤 SQL 文件名的模块前缀（例如 "aaemu_login"）。</param>
    /// <param name="res">输出参数，如果找到文件夹，则填充匹配的 SQL 文件路径列表。</param>
    /// <returns>如果找到包含匹配脚本的文件夹，则返回该文件夹的路径；否则返回空字符串。</returns>
    private static string FindUpdatesFolder(string moduleNamePrefix, out List<string> res)
    {
        // 向上遍历到根目录以查找合适的文件夹
        var currentDir = FileManager.AppPath;
        while (currentDir.Split(Path.DirectorySeparatorChar).Length > 1) // 只要当前目录不是根目录的直接子目录或根目录本身
        {
            var testDir = Path.Combine(currentDir, "SQL", "updates"); // 构建潜在的 updates 文件夹路径
            if (Directory.Exists(testDir)) // 检查文件夹是否存在
            {
                var tryFiles = Directory
                    .GetFiles(testDir, "*" + moduleNamePrefix + "*.sql", SearchOption.TopDirectoryOnly).ToList(); // 查找匹配的 SQL 文件
                if (tryFiles.Count > 0) // 如果找到文件
                {
                    res = tryFiles; // 设置输出文件列表
                    return testDir; // 返回文件夹路径
                }
            }

            try
            {
                var parentDir = Directory.GetParent(currentDir); // 获取上一级目录
                currentDir = parentDir?.FullName ?? string.Empty; // 更新当前目录为上一级目录
                if (string.IsNullOrEmpty(currentDir)) // 如果无法获取父目录，则停止
                    break;
            }
            catch (Exception ex) // 捕获可能的异常（例如权限问题）
            {
                Logger.Error(ex);
                currentDir = string.Empty; // 出错则停止
            }
        }

        res = new List<string>(); // 如果未找到，返回空列表
        return string.Empty; // 如果未找到，返回空字符串
    }
}
