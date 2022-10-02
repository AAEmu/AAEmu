using System.Collections.Concurrent;
using System.Net.Sockets;
using NLog;

namespace AAEmu.Commons.Network
{
    public class BufferManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        private int _numBytes;
        private byte[] _buffer;
        private ConcurrentStack<int> _freeIndexPool;
        private int _currentIndex;
        private int _bufferSize;

        public BufferManager(int totalBytes, int bufferSize)
        {
            _numBytes = totalBytes;
            _currentIndex = 0;
            _bufferSize = bufferSize;
            _freeIndexPool = new ConcurrentStack<int>();
        }

        public void InitBuffer()
        {
            _buffer = new byte[_numBytes];
        }

        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            lock (_freeIndexPool)
            {
                if (_freeIndexPool.Count > 0)
                {
                    int offset;
                    if (!_freeIndexPool.TryPop(out offset))
                        _log.Warn("TryPop from _freeIndexPool ConcurrentStack failed.");
                    args.SetBuffer(_buffer, offset, _bufferSize);
                }
                else
                {
                    if ((_numBytes - _bufferSize) < _currentIndex)
                        return false;
                    args.SetBuffer(_buffer, _currentIndex, _bufferSize);
                    _currentIndex += _bufferSize;
                }
            }
            return true;
        }

        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            _freeIndexPool.Push(args.Offset);
            //args.SetBuffer(null, 0, 0);
        }
    }
}
