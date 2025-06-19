using System.Collections.Generic;
using System.Net;

using NetCoreServer;

namespace AAEmu.Commons.Network.Core;

/// <summary>
/// 表示一个网络客户端，它连接到服务器并处理与 <see cref="ISession"/> 相关的通信。
/// 此类派生自 <see cref="NetCoreServer.TcpClient"/> 并实现 <see cref="ISession"/> 接口。
/// </summary>
public class Client : TcpClient, ISession
{
    private readonly Dictionary<string, object> _attributes = new(); // 用于存储与此会话关联的自定义属性。
    private BaseProtocolHandler _handler; // 处理此客户端网络事件的协议处理器。
    private uint _sessionId; // 此会话的唯一标识符。
    private IPAddress _ip; // 此客户端连接的本地 IP 地址。

    /// <summary>
    /// 获取此会话的 IP 地址。
    /// </summary>
    IPAddress ISession.Ip => _ip;

    /// <summary>
    /// 获取此会话的唯一会话 ID。
    /// </summary>
    uint ISession.SessionId => _sessionId;

    /// <summary>
    /// 异步发送数据包到连接的服务器。
    /// </summary>
    /// <param name="packet">要发送的字节数组数据包。</param>
    void ISession.SendPacket(byte[] packet)
    {
        SendAsync(packet);
    }

    /// <summary>
    /// 向此会话添加一个属性。
    /// </summary>
    /// <param name="name">属性的名称。</param>
    /// <param name="attribute">属性的值。</param>
    void ISession.AddAttribute(string name, object attribute)
    {
        _attributes.Add(name, attribute);
    }

    /// <summary>
    ///从此会话获取具有指定名称的属性。
    /// </summary>
    /// <param name="name">要获取的属性的名称。</param>
    /// <returns>属性的值，如果未找到则为 null。</returns>
    object ISession.GetAttribute(string name)
    {
        _attributes.TryGetValue(name, out var attribute);
        return attribute;
    }

    /// <summary>
    /// 从此会话中清除具有指定名称的属性。
    /// </summary>
    /// <param name="name">要清除的属性的名称。</param>
    void ISession.ClearAttribute(string name)
    {
        _attributes.Remove(name);
    }

    /// <summary>
    /// 关闭此客户端的连接。
    /// </summary>
    void ISession.Close()
    {
        Disconnect();
    }

    /// <summary>
    /// 初始化 <see cref="Client"/> 类的新实例。
    /// </summary>
    /// <param name="serverAddress">要连接的服务器的 IP 地址。</param>
    /// <param name="serverPort">要连接的服务器的端口号。</param>
    /// <param name="handler">用于处理此客户端网络事件的协议处理器。</param>
    public Client(IPAddress serverAddress, int serverPort, BaseProtocolHandler handler) : base(serverAddress, serverPort)
    {
        _handler = handler;
    }

    /// <summary>
    /// 获取与此客户端关联的协议处理器。
    /// </summary>
    /// <returns>协议处理器实例。</returns>
    public BaseProtocolHandler GetHandler()
    {
        return _handler;
    }

    /// <summary>
    /// 当客户端成功连接到服务器时调用。
    /// 初始化会话 ID 和 IP 地址，并通知协议处理器连接事件。
    /// </summary>
    protected override void OnConnected()
    {
        _sessionId = (uint)Socket.LocalEndPoint.GetHashCode();
        _ip = ((IPEndPoint)Socket.LocalEndPoint).Address;
        _handler.OnConnect(this);
    }

    /// <summary>
    /// 当客户端与服务器断开连接时调用。
    /// 通知协议处理器断开连接事件。
    /// </summary>
    protected override void OnDisconnected()
    {
        _handler.OnDisconnect(this);
    }

    /// <summary>
    /// 当从服务器接收到数据时调用。
    /// 将接收到的数据传递给协议处理器进行处理。
    /// </summary>
    /// <param name="buffer">包含接收数据的缓冲区。</param>
    /// <param name="offset">缓冲区中数据的起始偏移量。</param>
    /// <param name="size">接收到的字节数。</param>
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        _handler.OnReceive(this, buffer, (int)offset, (int)size);
    }

    /// <summary>
    /// 当数据成功发送到服务器后调用。
    /// </summary>
    /// <param name="sent">已发送的字节数。</param>
    /// <param name="pending">待发送的字节数。</param>
    protected override void OnSent(long sent, long pending)
    {
        base.OnSent(sent, pending);
    }
}
