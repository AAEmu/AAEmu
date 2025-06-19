namespace AAEmu.Commons.Network;

/// <summary>
/// 定义数据包日志记录的级别。
/// </summary>
public enum PacketLogLevel
{
    /// <summary>
    /// 关闭日志记录。
    /// </summary>
    Off,
    /// <summary>
    /// 记录非常详细的调试信息，通常用于追踪执行流程。
    /// </summary>
    Trace,
    /// <summary>
    /// 记录有助于开发和调试的详细信息。
    /// </summary>
    Debug,
    /// <summary>
    /// 记录常规操作信息。
    /// </summary>
    Info,
    /// <summary>
    /// 记录潜在的问题或非严重错误。
    /// </summary>
    Warning,
    /// <summary>
    /// 记录已发生的错误。
    /// </summary>
    Error,
    /// <summary>
    /// 记录导致应用程序终止的严重错误。
    /// </summary>
    Fatal
}
