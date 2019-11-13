using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AAEmu.Commons.Utils;
using NLog;

namespace AAEmu.Login.Utils
{
    public abstract class IdManager
    {
        protected static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private BitSet _freeIds;
        private int _freeIdCount;
        private int _nextFreeId;

        private readonly string _name;
        private readonly uint _firstId = 0x00000001;
        private readonly uint _lastId = 0xFFFFFFFF;
        private readonly uint[] _exclude;
        private readonly int _freeIdSize;
        private readonly bool _distinct;
        private readonly object _lock = new object();
        protected abstract IEnumerable<uint> ExtractUsedIds(bool isDistinct = false);

        public IdManager(string name, uint firstId, uint lastId, uint[] exclude,
            bool distinct = false)
        {
            _name = name;
            _firstId = firstId;
            _lastId = lastId;
            _exclude = exclude;
            _distinct = distinct;
            _freeIdSize = (int) (_lastId - _firstId);
            PrimeFinder.Init();
        }

        public bool Initialize()
        {
            try
            {
                _freeIds = new BitSet(PrimeFinder.NextPrime(100000));
                _freeIds.Clear();
                _freeIdCount = _freeIdSize;

                foreach (var usedObjectId in ExtractUsedIds(_distinct))
                {
                    if (_exclude.Contains(usedObjectId))
                        continue;
                    var objectId = (int) (usedObjectId - _firstId);
                    if (usedObjectId < _firstId)
                    {
                        _log.Warn("{0}: Object ID {1} in DB is less than {2}", _name, usedObjectId, _firstId);
                        continue;
                    }

                    if (objectId >= _freeIds.Count)
                        IncreaseBitSetCapacity(objectId + 1);
                    _freeIds.Set(objectId);
                    Interlocked.Decrement(ref _freeIdCount);
                }

                _nextFreeId = _freeIds.NextClear(0);
                _log.Info("{0} successfully initialized", _name);
            }
            catch (Exception e)
            {
                _log.Error("{0} could not be initialized correctly", _name);
                _log.Error(e);
                return false;
            }

            return true;
        }

        public virtual void ReleaseId(uint usedObjectId)
        {
            var objectId = (int) (usedObjectId - _firstId);
            if (objectId > -1)
            {
                _freeIds.Clear(objectId);
                if (_nextFreeId > objectId)
                    _nextFreeId = objectId;
                Interlocked.Increment(ref _freeIdCount);
            }
            else
                _log.Warn("{0}: release objectId {1} failed", _name, usedObjectId);
        }

        public virtual void ReleaseId(IEnumerable<uint> usedObjectIds)
        {
            foreach (var id in usedObjectIds)
                ReleaseId(id);
        }

        public uint GetNextId()
        {
            lock (_lock)
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
                            throw new Exception("Ran out of valid Id's.");
                    }
                }

                _nextFreeId = nextFree;
                return (uint) newId + _firstId;
            }
        }

        public uint[] GetNextId(int count)
        {
            var res = new uint[count];
            for (var i = 0; i < count; i++)
                res[i] = GetNextId();
            return res;
        }

        private void IncreaseBitSetCapacity()
        {
            var size = PrimeFinder.NextPrime(_freeIds.Count + _freeIdSize / 10);
            if (size > _freeIdSize)
                size = _freeIdSize;
            var newBitSet = new BitSet(size);
            newBitSet.Or(_freeIds);
            _freeIds = newBitSet;
        }

        private void IncreaseBitSetCapacity(int count)
        {
            var size = PrimeFinder.NextPrime(count);
            if (size > _freeIdSize)
                size = _freeIdSize;
            var newBitSet = new BitSet(size);
            newBitSet.Or(_freeIds);
            _freeIds = newBitSet;
        }
    }
}
