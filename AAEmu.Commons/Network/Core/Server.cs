using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Commons.Network.Core;

/// <summary>
/// 表示一个 TCP 服务器，用于监听传入的客户端连接并管理会话。
/// 此类派生自 <see cref="NetCoreServer.TcpServer"/>。
/// </summary>
public class Server : TcpServer
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private BaseProtocolHandler _protocolHandler; // 用于处理连接事件和数据的协议处理器。
    private readonly HashSet<Session> _sessions = new(); // 当前活动会话的集合。

    /// <summary>
    /// 获取与此服务器关联的协议处理器。
    /// </summary>
    /// <returns>协议处理器实例。</returns>
    public BaseProtocolHandler GetHandler() => _protocolHandler;

    /// <summary>
    /// 初始化 <see cref="Server"/> 类的新实例。
    /// </summary>
    /// <param name="address">服务器监听的 IP 地址。</param>
    /// <param name="port">服务器监听的端口号。</param>
    /// <param name="protocolHandler">用于处理连接事件和数据的协议处理器。</param>
    public Server(IPAddress address, int port, BaseProtocolHandler protocolHandler)
        : base(address, port)
    {
        _protocolHandler = protocolHandler;
    }

    /// <summary>
    /// 为新的客户端连接创建一个会话。
    /// </summary>
    /// <returns>新创建的 <see cref="Session"/> 对象。</returns>
    protected override TcpSession CreateSession() => new Session(this);

    /// <summary>
    /// 当服务器成功启动并开始监听时调用。
    /// </summary>
    protected override void OnStarted()
    {
        Logger.Info($"TCP server listening start on {Endpoint}");
    }

    /// <summary>
    /// 当服务器停止监听时调用。
    /// </summary>
    protected override void OnStopped()
    {
        Logger.Info("TCP server listener stopped!");
    }

    /// <summary>
    /// 当新的客户端连接到服务器时调用。
    /// 将新会话添加到活动会话集合中。
    /// </summary>
    /// <param name="session">已连接的 TCP 会话。</param>
    protected override void OnConnected(TcpSession session)
    {
        Logger.Info(
            $"Connect from {session.Socket.RemoteEndPoint} established, session id: {session.Id}");
        _sessions.Add((Session)session);
    }

    /// <summary>
    /// 当客户端与服务器断开连接时调用。
    /// 从活动会话集合中移除该会话。
    /// </summary>
    /// <param name="session">已断开连接的 TCP 会话。</param>
    protected override void OnDisconnected(TcpSession session)
    {
        Logger.Info($"Connect from session id: {session.Id} disconnected");
        _sessions.Remove((Session)session);
    }

    /// <summary>
    /// 当服务器发生套接字错误时调用。
    /// </summary>
    /// <param name="error">发生的套接字错误。</param>
    protected override void OnError(SocketError error)
    {
        Logger.Error($"TCP server SocketError: {error}");
    }

    /// <summary>
    /// 根据提供的谓词函数获取单个会话。
    /// </summary>
    /// <param name="func">用于测试每个会话是否满足条件的函数。</param>
    /// <returns>满足条件的第一个会话；如果未找到，则为 null。</returns>
    public Session GetSession(Func<Session, bool> func)
    {
        return _sessions.SingleOrDefault(func);
    }

    /// <summary>
    /// 获取所有当前活动会话的集合。
    /// </summary>
    /// <returns>一个包含所有活动会话的 <see cref="HashSet{Session}"/>。</returns>
    public HashSet<Session> GetSessions()
    {
        return _sessions;
    }

    /// <summary>
    /// 根据提供的谓词函数获取满足条件的所有会话。
    /// </summary>
    /// <param name="func">用于测试每个会话是否满足条件的函数。</param>
    /// <returns>一个包含所有满足条件的会话的 <see cref="IEnumerable{Session}"/>。</returns>
    public IEnumerable<Session> GetSessions(Func<Session, bool> func)
    {
        return _sessions.Where(func);
    }
}
