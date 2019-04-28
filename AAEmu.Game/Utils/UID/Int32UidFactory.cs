using System.Collections.Concurrent;

namespace AAEmu.Game.Utils.UID
{
    public class Int32UidFactory
    {
        private volatile int _nextUid = 1;
        private readonly ConcurrentQueue<int> _freeUidList = new ConcurrentQueue<int>();

        public Int32UidFactory(int val = 1)
        {
             _nextUid = val + 1;
        }

        public int Next()
        {
            int result;
            if (_freeUidList.TryDequeue(out result))
                return result;

            return _nextUid++;
        }

        public void ReleaseUniqueInt(int uid)
        {
            if ((int)uid == 0)
                return;

            _freeUidList.Enqueue(uid);
        }
    }
}
