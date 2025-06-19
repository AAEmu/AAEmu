using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;

namespace AAEmu.Commons.Network.Core;

/// <summary>
/// 定义网络会话的通用接口。
/// </summary>
public interface ISession
{
    /// <summary>
    /// 获取会话的 IP 地址。
    /// </summary>
    IPAddress Ip { get; }
    /// <summary>
    /// 获取会话的唯一标识符。
    /// </summary>
    uint SessionId { get; }
    /// <summary>
    /// 获取与此会话关联的底层套接字。
    /// </summary>
    Socket Socket { get; }
    /// <summary>
    /// 发送一个数据包。
    /// </summary>
    /// <param name="packet">要发送的字节数组数据包。</param>
    void SendPacket(byte[] packet);
    /// <summary>
    /// 向会话添加一个属性。
    /// </summary>
    /// <param name="name">属性的名称。</param>
    /// <param name="attribute">属性的值。</param>
    void AddAttribute(string name, object attribute);
    /// <summary>
    /// 从会话中获取具有指定名称的属性。
    /// </summary>
    /// <param name="name">要获取的属性的名称。</param>
    /// <returns>属性的值，如果未找到则为 null。</returns>
    object GetAttribute(string name);
    /// <summary>
    /// 从会话中清除具有指定名称的属性。
    /// </summary>
    /// <param name="name">要清除的属性的名称。</param>
    void ClearAttribute(string name);
    /// <summary>
    /// 关闭会话连接。
    /// </summary>
    void Close();
}

/// <summary>
/// 表示服务器上的一个客户端连接会话。
/// 此类派生自 <see cref="NetCoreServer.TcpSession"/> 并实现 <see cref="ISession"/> 接口。
/// </summary>
public class Session : TcpSession, ISession
{
    private readonly Dictionary<string, object> _attributes = new(); // 用于存储与此会话关联的自定义属性。

    /// <summary>
    /// 获取与此会话关联的协议处理器。
    /// </summary>
    public BaseProtocolHandler ProtocolHandler { get; private set; }
    /// <summary>
    /// 获取客户端的远程网络端点。
    /// </summary>
    public IPEndPoint RemoteEndPoint { get; private set; }
    /// <summary>
    /// 获取此会话的唯一会话 ID。
    /// </summary>
    public uint SessionId { get; private set; }
    /// <summary>
    /// 获取客户端的 IP 地址。
    /// </summary>
    public IPAddress Ip { get; private set; }

    /// <summary>
    /// 初始化 <see cref="Session"/> 类的新实例。
    /// </summary>
    /// <param name="server">创建此会话的服务器实例。</param>
    public Session(Server server) : base(server)
    {
        ProtocolHandler = server.GetHandler();
    }

    /// <summary>
    /// 当正在建立与客户端的连接时调用。
    /// 初始化会话属性（远程端点、会话 ID、IP）并通知协议处理器连接事件。
    /// </summary>
    protected override void OnConnecting()
    {
        RemoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint;
        SessionId = (uint)RemoteEndPoint.GetHashCode();
        Ip = RemoteEndPoint.Address;
        ProtocolHandler?.OnConnect(this);
    }

    /// <summary>
    /// 当与客户端的连接已成功建立时调用。
    /// 注意：核心连接逻辑已移至 OnConnecting，以解决 NetCoreServer 中 OnReceived 可能在 OnConnected 之前触发的问题。
    /// </summary>
    protected override void OnConnected()
    {
        // 由于 TcpSession 中的一个错误，OnReceived 可能会在 OnConnected 之前发生，因此移至 OnConnecting。
        //_remoteEndPoint = (IPEndPoint)Socket.RemoteEndPoint; // 获取远程端点
        //_sessionId = (uint)RemoteEndPoint.GetHashCode(); // 获取会话ID
        //_ip = RemoteEndPoint.Address; // 获取IP地址
        //ProtocolHandler?.OnConnect(this); // 调用连接处理器
    }

    /// <summary>
    /// 当与客户端的连接断开时调用。
    /// 通知协议处理器断开连接事件。
    /// </summary>
    protected override void OnDisconnected()
    {
        ProtocolHandler?.OnDisconnect(this);
    }

    /// <summary>
    /// 当从客户端接收到数据时调用。
    /// 将接收到的数据传递给协议处理器进行处理。
    /// </summary>
    /// <param name="buffer">包含接收数据的缓冲区。</param>
    /// <param name="offset">缓冲区中数据的起始偏移量。</param>
    /// <param name="size">接收到的字节数。</param>
    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        ProtocolHandler?.OnReceive(this, buffer, (int)offset, (int)size);
    }

    /// <summary>
    /// 当数据已排队等待发送到客户端后调用。
    /// </summary>
    /// <param name="sent">已发送（排队）的字节数。</param>
    /// <param name="pending">待发送（排队）的字节数。</param>
    protected override void OnSent(long sent, long pending)
    {
    }

    /// <summary>
    /// 当在会话期间发生套接字错误时调用。
    /// </summary>
    /// <param name="error">发生的套接字错误。</param>
    protected override void OnError(SocketError error)
    {
    }

    /// <summary>
    /// 异步发送一个 <see cref="PacketStream"/> 消息到客户端。
    /// </summary>
    /// <param name="message">要发送的消息。</param>
    public virtual void SendMessage(PacketStream message)
    {
        // var stream = new PacketStream(); // 创建一个新的数据包流
        // message.Write(stream); // 将消息写入流
        SendAsync(message);
    }

    /// <summary>
    /// 异步发送一个字节数组到客户端。
    /// </summary>
    /// <param name="buffer">包含要发送数据的缓冲区。</param>
    /// <returns>如果发送操作成功启动，则为 true；否则为 false。</returns>
    public override bool SendAsync(byte[] buffer)
    {
        // TODO 发送到队列
        return SendAsync(buffer, 0L, buffer.Length);
    }

    /// <summary>
    /// 向此会话添加一个属性。
    /// </summary>
    /// <param name="name">属性的名称。</param>
    /// <param name="attribute">属性的值。</param>
    public void AddAttribute(string name, object attribute)
    {
        _attributes.Add(name, attribute);
    }

    /// <summary>
    /// 从此会话获取具有指定名称的属性。
    /// </summary>
    /// <param name="name">要获取的属性的名称。</param>
    /// <returns>属性的值，如果未找到则为 null。</returns>
    public object GetAttribute(string name)
    {
        _attributes.TryGetValue(name, out var attribute);
        return attribute;
    }

    /// <summary>
    /// 从此会话中清除具有指定名称的属性。
    /// </summary>
    /// <param name="name">要清除的属性的名称。</param>
    public void ClearAttribute(string name)
    {
        _attributes.Remove(name);
    }

    /// <summary>
    /// 关闭此会话的连接。
    /// </summary>
    public void Close()
    {
        Disconnect();
    }

    /// <summary>
    /// 异步发送一个数据包到客户端。
    /// </summary>
    /// <param name="packet">要发送的字节数组数据包。</param>
    public void SendPacket(byte[] packet)
    {
        SendAsync(packet);
    }
}
