using System.Collections.Concurrent;

using AAEmu.World.Core.Network;

using NLog;

namespace AAEmu.World.Core.Zone;

/// <summary>
/// TCP Zone connections indexed by session and by (zoneId, instanceId).
/// Two hosts of the same zone key stay registered when their instance ids differ.
/// </summary>
public sealed class ZoneConnectionRegistry
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, ZoneConnection> _bySession = new();
    private readonly ConcurrentDictionary<ZoneInstanceKey, ZoneConnection> _byInstance = new();

    public void Add(ZoneConnection connection) => _bySession[connection.Id] = connection;

    public void Remove(uint sessionId)
    {
        if (!_bySession.TryRemove(sessionId, out var connection))
            return;

        var key = KeyOf(connection);
        if (key.ZoneId != 0 &&
            _byInstance.TryGetValue(key, out var mapped) &&
            ReferenceEquals(mapped, connection))
        {
            _byInstance.TryRemove(key, out _);
            Logger.Info(
                "Zone registry remove zoneId={0} instanceId={1} session={2}; loadedCount={3}",
                key.ZoneId, key.InstanceId, sessionId, LoadedCount);
        }
    }

    /// <summary>
    /// Bind after join (and refresh on ZoneLoaded). Duplicate (zoneId, instanceId) replaces the
    /// previous mapping and warns. A second copy of the same zone with a different instance id
    /// is added alongside, not instead of, the first.
    /// </summary>
    public void Index(ZoneConnection connection)
    {
        if (connection.ZoneId == 0)
            return;

        var key = KeyOf(connection);
        _byInstance.AddOrUpdate(
            key,
            connection,
            (_, existing) =>
            {
                if (!ReferenceEquals(existing, connection))
                {
                    Logger.Warn(
                        "ZoneId {0} instanceId {1} remapped session {2} → {3} (previous dropped from registry index)",
                        key.ZoneId, key.InstanceId, existing.Id, connection.Id);
                }

                return connection;
            });
    }

    public ZoneConnection? Get(uint sessionId) =>
        _bySession.TryGetValue(sessionId, out var connection) ? connection : null;

    /// <summary>ZoneLoaded connection for this exact copy, or null.</summary>
    public ZoneConnection? GetLoaded(uint zoneId, uint instanceId)
    {
        if (zoneId == 0)
            return null;
        if (!_byInstance.TryGetValue(new ZoneInstanceKey(zoneId, instanceId), out var connection))
            return null;
        return connection.State >= ZoneConnectionState.ZoneLoaded ? connection : null;
    }

    /// <summary>Joined (or loaded) connection for this copy — not enter-ready unless ZoneLoaded.</summary>
    public ZoneConnection? GetJoined(uint zoneId, uint instanceId)
    {
        if (zoneId == 0)
            return null;
        if (!_byInstance.TryGetValue(new ZoneInstanceKey(zoneId, instanceId), out var connection))
            return null;
        return connection.State >= ZoneConnectionState.Joined ? connection : null;
    }

    /// <summary>
    /// Unique host for a zone key at or above <paramref name="minState"/>. One connection wins.
    /// Several copies of the same key return instance 0 if present, otherwise null.
    /// </summary>
    public ZoneConnection? GetUnique(uint zoneId, ZoneConnectionState minState)
    {
        if (zoneId == 0)
            return null;

        ZoneConnection? only = null;
        ZoneConnection? instanceZero = null;
        var matched = 0;
        foreach (var connection in _byInstance.Values)
        {
            if (connection.ZoneId != zoneId || connection.State < minState)
                continue;
            matched++;
            only = connection;
            if (connection.InstanceId == 0)
                instanceZero = connection;
        }

        if (matched == 1)
            return only;
        if (matched > 1)
            return instanceZero;
        return null;
    }

    public ZoneConnection? GetUniqueLoaded(uint zoneId) =>
        GetUnique(zoneId, ZoneConnectionState.ZoneLoaded);

    public IEnumerable<ZoneConnection> All => _bySession.Values;

    public int LoadedCount => _byInstance.Values.Count(z => z.State >= ZoneConnectionState.ZoneLoaded);

    private static ZoneInstanceKey KeyOf(ZoneConnection connection) =>
        new(connection.ZoneId, connection.InstanceId);
}
