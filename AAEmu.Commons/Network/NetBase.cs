using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network;

/// <summary>
/// 定义网络基础操作的接口，用于处理会话生命周期和数据事件。
/// </summary>
public interface INetBase
{
    /// <summary>
    /// 当新的客户端会话连接时调用。
    /// </summary>
    /// <param name="session">已连接的会话。</param>
    void OnConnect(Session session);
    /// <summary>
    /// 当从会话接收到数据时调用。
    /// </summary>
    /// <param name="session">接收数据的会话。</param>
    /// <param name="buf">包含接收数据的缓冲区。</param>
    /// <param name="bytes">接收到的字节数。</param>
    void OnReceive(Session session, byte[] buf, int bytes);
    /// <summary>
    /// 当向会话发送数据后调用。
    /// </summary>
    /// <param name="session">发送数据的会话。</param>
    /// <param name="buf">包含已发送数据的缓冲区。</param>
    /// <param name="offset">缓冲区中数据的起始偏移量。</param>
    /// <param name="bytes">已发送的字节数。</param>
    void OnSend(Session session, byte[] buf, int offset, int bytes);
    /// <summary>
    /// 当客户端会话断开连接时调用。
    /// </summary>
    /// <param name="session">已断开连接的会话。</param>
    void OnDisconnect(Session session);
    /// <summary>
    /// 从活动会话管理中移除指定的会话。
    /// </summary>
    /// <param name="session">要移除的会话。</param>
    void RemoveSession(Session session);
}

