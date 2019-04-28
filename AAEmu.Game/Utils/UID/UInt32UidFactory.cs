/*
  That file part of Code Monsters framework.
  Cerium Unity 2015 © 
*/
using System.Collections.Concurrent;

namespace AAEmu.Game.Utils.UID
{
    public class UInt32UidFactory
    {
        private volatile uint _nextUid = 1;
        private readonly ConcurrentQueue<uint> _freeUidList = new ConcurrentQueue<uint>();

        public UInt32UidFactory(uint val = 1U)
        {
            if(val != 1)
              _nextUid = val + 1;
        }

        public uint Next()
        {
            uint result;
            if (_freeUidList.TryDequeue(out result))
                return result;

            return _nextUid++;
        }

        public void ReleaseUniqueInt(uint uid)
        {
            if ((int)uid == 0)
                return;

            _freeUidList.Enqueue(uid);
        }
    }
}
