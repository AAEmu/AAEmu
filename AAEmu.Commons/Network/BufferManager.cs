using System.Collections.Concurrent;
using System.Net.Sockets;
using NLog;

namespace AAEmu.Commons.Network;

/// <summary>
/// 管理用于网络操作的大型预分配字节缓冲区。
/// 这有助于通过重用缓冲区段来减少内存分配和碎片。
/// </summary>
public class BufferManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private int _numBytes;  // 分配的缓冲区总字节数。
    private byte[] _buffer; // 底层大字节数组。
    private ConcurrentStack<int> _freeIndexPool; // 存储已释放缓冲区段的起始偏移量，以便重用。
    private int _currentIndex; // 指向当前未分配部分的起始偏移量。
    private int _bufferSize; // 每个 SocketAsyncEventArgs 使用的缓冲区段的大小。

    /// <summary>
    /// 初始化 BufferManager。
    /// </summary>
    /// <param name="totalBytes">要分配给缓冲池的总字节数。</param>
    /// <param name="bufferSize">每个网络操作使用的缓冲区段的大小。</param>
    public BufferManager(int totalBytes, int bufferSize)
    {
        _numBytes = totalBytes;
        _currentIndex = 0;
        _bufferSize = bufferSize;
        _freeIndexPool = new ConcurrentStack<int>();
    }

    /// <summary>
    /// 初始化底层字节缓冲区。
    /// 应在 BufferManager 实例化后但在首次使用前调用。
    /// </summary>
    public void InitBuffer()
    {
        _buffer = new byte[_numBytes];
    }

    /// <summary>
    /// 为 SocketAsyncEventArgs 对象设置缓冲区。
    /// 从池中取出一个空闲的段，或者如果池为空则从主缓冲区中分配一个新的段。
    /// </summary>
    /// <param name="args">要为其设置缓冲区的 SocketAsyncEventArgs 对象。</param>
    /// <returns>如果成功设置缓冲区则为 true；否则为 false（例如，如果没有足够的空间）。</returns>
    public bool SetBuffer(SocketAsyncEventArgs args)
    {
        lock (_freeIndexPool) // 确保对 _freeIndexPool 和 _currentIndex 的访问是线程安全的。
        {
            if (!_freeIndexPool.IsEmpty) // 优先重用已释放的段
            {
                if (!_freeIndexPool.TryPop(out var offset))
                    Logger.Warn("TryPop from _freeIndexPool ConcurrentStack failed."); // 理论上在 IsEmpty 检查后不应失败
                args.SetBuffer(_buffer, offset, _bufferSize);
            }
            else // 如果池为空，则分配新的段
            {
                if ((_numBytes - _bufferSize) < _currentIndex) // 检查是否有足够的空间
                    return false;
                args.SetBuffer(_buffer, _currentIndex, _bufferSize);
                _currentIndex += _bufferSize; // 更新下一个可用段的起始位置
            }
        }
        return true;
    }

    /// <summary>
    /// 释放先前分配给 SocketAsyncEventArgs 对象的缓冲区段。
    /// 将段的偏移量添加回空闲池以便重用。
    /// </summary>
    /// <param name="args">包含要释放的缓冲区段的 SocketAsyncEventArgs 对象。</param>
    public void FreeBuffer(SocketAsyncEventArgs args)
    {
        _freeIndexPool.Push(args.Offset);
        //args.SetBuffer(null, 0, 0); // 将缓冲区设置为空
    }
}
