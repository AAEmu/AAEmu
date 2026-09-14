using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;

using NLog;

namespace AAEmu.Game.Utils;

public class IdManager
{
    // ReSharper disable once MemberCanBePrivate.Global
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private BitSet _freeIds;
    private int _freeIdCount;
    private int _nextFreeId;
    private bool _initialized;

    private readonly string _name;
    private readonly uint _firstId;
    private readonly uint _lastId;
    private readonly uint[] _exclude;
    private readonly int _freeIdSize;
    private readonly string[,] _objTables;
    private readonly bool _distinct;
    private readonly object _lock = new();
    private Func<uint[]> _usedIdsLoaderForTest;

    // ReSharper disable once MemberCanBeProtected.Global
    public IdManager(string name, uint firstId, uint lastId, string[,] objTables, uint[] exclude, bool distinct = false)
    {
        _name = name;
        _firstId = firstId;
        _lastId = lastId;
        _objTables = objTables;
        _exclude = exclude;
        _distinct = distinct;
        _freeIdSize = (int)(_lastId - _firstId);
        PrimeFinder.Init();
    }

    /// <summary>Called by the ManagerOrchestrator in Stage 2, delegating to Initialize().</summary>
    public virtual void Load()
    {
        if (!Initialize())
            throw new GameException($"{_name} could not load its persisted ID reservations.");
    }

    /// <summary>
    /// Initializes the IdManager for use by resetting the Ids and grabbing data from the database if needed
    /// </summary>
    /// <param name="forceReset">When true forces the re-initialization even if it was previously initialized already</param>
    /// <returns></returns>
    public bool Initialize(bool forceReset = false)
    {
        if (_initialized && forceReset == false)
            return true;

        try
        {
            var freeIds = new BitSet(PrimeFinder.NextPrime(100000));
            freeIds.Clear();
            _freeIds = freeIds;
            _freeIdCount = _freeIdSize;

            // A failed read must leave this allocator unavailable. Treating an unknown live set as
            // empty can hand out an ID that already belongs to a persisted object.
            var allUsedObjects = _usedIdsLoaderForTest?.Invoke() ?? ExtractUsedObjectIdTable();

            foreach (var usedObjectId in allUsedObjects.Distinct())
            {
                if (_exclude.Contains(usedObjectId))
                    continue;
                var objectId = (int)(usedObjectId - _firstId);
                if (usedObjectId < _firstId)
                {
                    Logger.Warn($"{_name}: Object ID {usedObjectId} in DB is less than {_firstId}");
                    continue;
                }
                if (usedObjectId >= _lastId)
                {
                    Logger.Warn($"{_name}: Object ID {usedObjectId} in DB is outside [{_firstId}, {_lastId})");
                    continue;
                }

                if (objectId >= _freeIds.Count)
                    IncreaseBitSetCapacity(objectId + 1);
                _freeIds.Set(objectId);
                Interlocked.Decrement(ref _freeIdCount);
            }

            _nextFreeId = _freeIds.NextClear(0);
            Logger.Info($"{_name} successfully initialized");
        }
        catch (Exception e)
        {
            _freeIds = null;
            _freeIdCount = 0;
            _nextFreeId = -1;
            _initialized = false;
            Logger.Error($"{_name} could not be initialized correctly");
            Logger.Error(e);
            return false;
        }

        _initialized = true;
        return true;
    }

    private uint[] ExtractUsedObjectIdTable()
    {
        if (_objTables.Length < 2)
            return [];

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        var query = "SELECT " + (_distinct ? "DISTINCT " : "") + _objTables[0, 1] + ", 0 AS i FROM " +
                    _objTables[0, 0];
        for (var i = 1; i < _objTables.Length / 2; i++)
            query += " UNION ALL SELECT " + (_distinct ? "DISTINCT " : "") + _objTables[i, 1] + ", " + i +
                     " FROM " + _objTables[i, 0];

        command.CommandText = query;
        command.Prepare();
        var result = new HashSet<uint>();
        var duplicateCount = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!result.Add(reader.GetUInt32(0)))
                    duplicateCount++;
            }
        }
        if (duplicateCount > 0)
            Logger.Warn($"{_name}: found {duplicateCount} duplicate persisted IDs across its shared tables; reserving each ID once");
        Logger.Info($"{_name}: Successfully extracted {result.Count} unique used id's from data tables.");
        return result.ToArray();
    }

    internal void SetUsedIdsLoaderForTest(Func<uint[]> loader) => _usedIdsLoaderForTest = loader;

    public void ReleaseId(uint usedObjectId)
    {
        lock (_lock)
        {
            if (_freeIds == null)
            {
                // Nothing was ever handed out, so there is nothing to give back. Releasing an Id must never be
                // fatal, it happens on teardown paths where throwing would take the caller (or process) down.
                Logger.Warn($"{_name}: release objectId {usedObjectId} skipped, manager was never initialized");
                return;
            }

            var objectId = (int)(usedObjectId - _firstId);
            if (objectId < 0 || objectId >= _freeIds.Count)
            {
                // Zone mirror bcIds and other out-of-pool ids must not clear the BitSet.
                Logger.Warn($"{_name}: release skipped for out-of-range id {usedObjectId} (idx={objectId}, size={_freeIds.Count})");
                return;
            }

            _freeIds.Clear(objectId);
            if (_nextFreeId > objectId)
                _nextFreeId = objectId;
            Interlocked.Increment(ref _freeIdCount);
        }
    }

    public void ReleaseId(IEnumerable<uint> usedObjectIds)
    {
        foreach (var id in usedObjectIds)
            ReleaseId(id);
    }

    /// <summary>
    /// Mark [fromInclusive, toExclusive) as used so <see cref="GetNextId"/> skips them.
    /// Used to reserve the dedicate unit-table band for Zone NPC mirrors.
    /// </summary>
    public void ReserveRange(uint fromInclusive, uint toExclusive)
    {
        if (toExclusive <= fromInclusive)
            return;

        lock (_lock)
        {
            if (!_initialized || _freeIds == null)
                throw new InvalidOperationException($"{_name} is unavailable because initialization did not complete.");

            for (var id = fromInclusive; id < toExclusive; id++)
            {
                if (id < _firstId || id >= _lastId)
                    continue;

                var objectId = (int)(id - _firstId);
                if (objectId >= _freeIds.Count)
                    IncreaseBitSetCapacity(objectId + 1);

                if (_freeIds.Get(objectId))
                    continue;

                _freeIds.Set(objectId);
                Interlocked.Decrement(ref _freeIdCount);
            }

            if (_nextFreeId >= 0 && _freeIds.Get(_nextFreeId))
                _nextFreeId = _freeIds.NextClear(_nextFreeId);

            Logger.Info($"{_name}: reserved id band [{fromInclusive}, {toExclusive}) for zone mirrors");
        }
    }

    public uint GetNextId()
    {
        lock (_lock)
        {
            if (!_initialized || _freeIds == null || _nextFreeId < 0)
                throw new InvalidOperationException($"{_name} is unavailable because initialization did not complete.");
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
                        throw new GameException("Ran out of valid Id's.");
                }
            }

            _nextFreeId = nextFree;
            return (uint)newId + _firstId;
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
