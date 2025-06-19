using System;
using AAEmu.Commons.Models;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Commons.Utils.DB;

/// <summary>
/// 提供用于处理 MySQL 数据库连接的静态实用方法。
/// </summary>
public static class MySQL
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static string s_connectionString; // 存储用于创建 MySQL 连接的连接字符串。

    /// <summary>
    /// 初始化 <see cref="MySQL"/> 类的静态成员。
    /// 使用默认设置（通常为 null，表示使用 MySqlConnectionStringBuilder 的默认值）配置连接字符串。
    /// </summary>
    static MySQL()
    {
        SetConfiguration(null);
    }

    /// <summary>
    /// 创建并打开一个新的 <see cref="MySqlConnection"/>。
    /// </summary>
    /// <returns>如果连接成功打开，则为打开的 <see cref="MySqlConnection"/> 对象；否则为 null。</returns>
    public static MySqlConnection CreateConnection()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var connection = new MySqlConnection(s_connectionString);
#pragma warning restore CA2000 // Dispose objects before losing scope
        try
        {
            connection.Open();
        }
        catch (Exception e)
        {
            Logger.Fatal($"Error on DB connect: {e.Message}");
            return null;
        }

        return connection;
    }

    /// <summary>
    /// 关闭指定的 <see cref="MySqlConnection"/>。
    /// </summary>
    /// <param name="connection">要关闭的 MySQL 连接。</param>
    public static void Close(MySqlConnection connection)
    {
        connection.Close();
    }

    /// <summary>
    /// 根据提供的设置配置 MySQL 连接字符串。
    /// 如果提供的设置为 null，则将使用 <see cref="MySqlConnectionStringBuilder"/> 的默认值。
    /// </summary>
    /// <param name="mySqlConnectionSettings">包含 MySQL 连接参数的设置对象。</param>
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
