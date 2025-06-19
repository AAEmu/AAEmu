namespace AAEmu.Commons.Models;

/// <summary>
/// 存储 MySQL 数据库连接所需的设置。
/// </summary>
public class MySqlConnectionSettings
{
    /// <summary>
    /// 获取或设置数据库服务器的主机名或 IP 地址。
    /// </summary>
    public string Host { get; set; }
    /// <summary>
    /// 获取或设置数据库服务器的端口号。
    /// </summary>
    public ushort Port { get; set; }
    /// <summary>
    /// 获取或设置连接数据库时使用的用户名。
    /// </summary>
    public string User { get; set; }
    /// <summary>
    /// 获取或设置连接数据库时使用的密码。
    /// </summary>
    public string Password { get; set; }
    /// <summary>
    /// 获取或设置要连接的特定数据库的名称。
    /// </summary>
    public string Database { get; set; }
}
