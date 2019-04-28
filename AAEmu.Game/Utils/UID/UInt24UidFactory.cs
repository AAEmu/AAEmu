using System.Collections.Concurrent;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Utils.UID
{
    public class UInt24UidFactory
    {
        private volatile uint _nextUid = 1;
        private readonly ConcurrentQueue<Uint24> _freeUidList = new ConcurrentQueue<Uint24>();

        public UInt24UidFactory(Uint24 val = default(Uint24))
        {
            if(val != 1)
              _nextUid = val + 1;
        }

        public Uint24 Next()
        {
            Uint24 result;
            if (_freeUidList.TryDequeue(out result))
                return result;

            return _nextUid++;
        }

        public void ReleaseUniqueInt(Uint24 uid24)
        {
            if ((Uint24)uid24 == 0)
                return;

            _freeUidList.Enqueue(uid24);
        }
    }
}
