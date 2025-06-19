using AAEmu.Commons.Network.Core;

namespace AAEmu.Commons.Network;

/// <summary>
/// 定义网络协议处理程序的基类。
/// 派生类应重写这些虚拟方法以实现特定的协议逻辑。
/// </summary>
public abstract class BaseProtocolHandler
{
    /// <summary>
    /// 当与会话建立新连接时调用。
    /// </summary>
    /// <param name="session">已连接的会话。</param>
    public virtual void OnConnect(ISession session)
    {
    }

    /// <summary>
    /// 当从会话接收到数据时调用。
    /// </summary>
    /// <param name="session">接收数据的会话。</param>
    /// <param name="buf">包含接收数据的缓冲区。</param>
    /// <param name="offset">缓冲区中数据的起始偏移量。</param>
    /// <param name="bytes">接收到的字节数。</param>
    public virtual void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
    }

    /// <summary>
    /// 当向会话发送数据后调用（注意：这通常表示数据已排队等待发送，而非已在网络层面发送完成）。
    /// </summary>
    /// <param name="session">发送数据的会话。</param>
    /// <param name="buf">包含已发送数据的缓冲区。</param>
    /// <param name="offset">缓冲区中数据的起始偏移量。</param>
    /// <param name="bytes">已发送的字节数。</param>
    public virtual void OnSend(ISession session, byte[] buf, int offset, int bytes)
    {
    }

    /// <summary>
    /// 当与会话的连接断开时调用。
    /// </summary>
    /// <param name="session">已断开连接的会话。</param>
    public virtual void OnDisconnect(ISession session)
    {
    }
}
