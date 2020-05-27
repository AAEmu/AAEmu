using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO.Bits;

namespace AAEmu.Commons.Utils.IdContainers
{
    public class BitIdContainer : IIdContainer
    {
        private readonly SemaphoreSlim _slim = new SemaphoreSlim(1);
        private readonly uint _firstId;
        private readonly int _freeIdSize;

        private BitSet _freeIds;
        private int _freeIdCount;
        private int _nextFreeId;

        public BitIdContainer(uint firstId, uint lastId)
        {
            _firstId = firstId;
            _freeIdSize = (int)(lastId - _firstId);

            _freeIds = new BitSet(Math.PrimeFinder.NextPrime(500000));
            _freeIds.Clear();
            _freeIdCount = _freeIdSize;
            _nextFreeId = _freeIds.NextClear(0);
        }

        public void Initialize(ulong[] usedObjectIds)
        {
            foreach (var usedObjectId in usedObjectIds)
            {
                var objectId = (int)(usedObjectId - _firstId);
                if (usedObjectId < _firstId)
                    throw new Exception($"Object ID {usedObjectId} in DB is less than {_firstId}");

                if (objectId >= _freeIds.Count)
                    IncreaseBitSetCapacity(objectId + 1);
                _freeIds.Set(objectId);
                Interlocked.Decrement(ref _freeIdCount);
            }

            _nextFreeId = _freeIds.NextClear(0);
        }

        public async Task<ulong> GetNextId()
        {
            await _slim.WaitAsync();

            try
            {
                var newId = _nextFreeId;
                _freeIds.Set(newId);
                Interlocked.Decrement(ref _freeIdCount);

                var nextFree = _freeIds.NextClear(newId);

                while (nextFree < 0)
                {
                    nextFree = _freeIds.NextClear(0);
                    if (nextFree < 0)
                    {
                        if (_freeIds.Count < _freeIdSize)
                            IncreaseBitSetCapacity();
                        else
                            throw new OutOfMemoryException();
                    }
                }

                _nextFreeId = nextFree;
                return (uint)newId + _firstId;
            }
            finally
            {
                _slim.Release();
            }
        }

        public async Task<ulong[]> GetNextId(int count)
        {
            var res = new ulong[count];
            for (var i = 0; i < count; i++)
                res[i] = await GetNextId();
            return res;
        }

        public bool ReleaseId(ulong usedObjectId)
        {
            var objectId = (int)(usedObjectId - _firstId);
            if (objectId > -1)
            {
                _freeIds.Clear(objectId);
                if (_nextFreeId > objectId)
                    _nextFreeId = objectId;
                Interlocked.Increment(ref _freeIdCount);
                return true;
            }

            return false;
        }

        public bool[] ReleaseId(ulong[] usedObjectIds)
        {
            var res = new bool[usedObjectIds.Length];
            for (var i = 0; i < usedObjectIds.Length; i++)
                res[i] = ReleaseId(usedObjectIds[i]);
            return res;
        }

        private void IncreaseBitSetCapacity()
        {
            var size = Math.PrimeFinder.NextPrime(_freeIds.Count + _freeIdSize / 10);
            if (size > _freeIdSize)
                size = _freeIdSize;
            var newBitSet = new BitSet(size);
            newBitSet.Or(_freeIds);
            _freeIds = newBitSet;
        }

        private void IncreaseBitSetCapacity(int count)
        {
            var size = Math.PrimeFinder.NextPrime(count);
            if (size > _freeIdSize)
                size = _freeIdSize;
            var newBitSet = new BitSet(size);
            newBitSet.Or(_freeIds);
            _freeIds = newBitSet;
        }
    }
}
