using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using NLog;

namespace AAEmu.Commons.Network;

public class SocketAsyncEventArgsPool
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    private ConcurrentStack<SocketAsyncEventArgs> _pool;

    public int Count => _pool.Count;
    public bool IsEmpty => _pool.IsEmpty;

    public SocketAsyncEventArgsPool()
    {
        _pool = new ConcurrentStack<SocketAsyncEventArgs>();
    }

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

    public SocketAsyncEventArgs Pop()
    {
        if (!_pool.TryPop(out var output))
            Logger.Error("TryPop from SocketAsyncEventArgs ConcurrentStack failed.");
        return output;
    }
}
