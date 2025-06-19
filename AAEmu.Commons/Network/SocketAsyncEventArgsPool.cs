using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using NLog;

namespace AAEmu.Commons.Network;

/// <summary>
/// 表示一个 <see cref="SocketAsyncEventArgs"/> 对象的可重用池。
/// 用于通过重用这些对象来减少网络操作中的内存分配。
/// </summary>
public class SocketAsyncEventArgsPool
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private ConcurrentStack<SocketAsyncEventArgs> _pool; // 用于存储池中 SocketAsyncEventArgs 对象的并发栈。

    /// <summary>
    /// 获取池中当前 <see cref="SocketAsyncEventArgs"/> 对象的数量。
    /// </summary>
    public int Count => _pool.Count;
    /// <summary>
    /// 获取一个值，该值指示池是否为空。
    /// </summary>
    public bool IsEmpty => _pool.IsEmpty;

    /// <summary>
    /// 初始化 <see cref="SocketAsyncEventArgsPool"/> 类的新实例。
    /// </summary>
    public SocketAsyncEventArgsPool()
    {
        _pool = new ConcurrentStack<SocketAsyncEventArgs>();
    }

    /// <summary>
    /// 将一个 <see cref="SocketAsyncEventArgs"/> 对象添加到池中。
    /// </summary>
    /// <param name="item">要添加到池中的 <see cref="SocketAsyncEventArgs"/> 对象。</param>
    /// <exception cref="ArgumentNullException">如果 <paramref name="item"/> 为 null。</exception>
    public void Push(SocketAsyncEventArgs item)
    {
        if (item == null)
        {
            Logger.Error("Items added to a SocketAsyncEventArgsPool cannot be null.");
            throw
                new ArgumentNullException(nameof(item));
        }
        _pool.Push(item);
    }

    /// <summary>
    /// 从池中移除并返回一个 <see cref="SocketAsyncEventArgs"/> 对象。
    /// </summary>
    /// <returns>从池中弹出的 <see cref="SocketAsyncEventArgs"/> 对象；如果池为空，则可能为 null（取决于 TryPop 的行为）。</returns>
    public SocketAsyncEventArgs Pop()
    {
        if (!_pool.TryPop(out var output))
            Logger.Error("TryPop from SocketAsyncEventArgs ConcurrentStack failed."); // 如果池为空，TryPop 会失败并返回 false。
        return output;
    }
}
