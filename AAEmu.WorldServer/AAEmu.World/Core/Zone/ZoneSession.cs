using System.Collections.Concurrent;

using AAEmu.Commons.Utils;
using AAEmu.World.Core.Network;

using NLog;

namespace AAEmu.World.Core.Zone;

/// <summary>
/// All TCP Zone connections. Indexed by session id and by dedicate ZoneId (ZWJoin.id).
/// </summary>
public class ZoneSession : Singleton<ZoneSession>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, ZoneConnection> _bySession = new();
    /// <summary>Zone key → connection. Last ZoneLoaded for a ZoneId wins.</summary>
    private readonly ConcurrentDictionary<uint, ZoneConnection> _byZoneId = new();

    public void Add(ZoneConnection connection) => _bySession[connection.Id] = connection;

    public void Remove(uint sessionId)
    {
        if (!_bySession.TryRemove(sessionId, out var connection))
            return;

        if (connection.ZoneId != 0 &&
            _byZoneId.TryGetValue(connection.ZoneId, out var mapped) &&
            ReferenceEquals(mapped, connection))
        {
            _byZoneId.TryRemove(connection.ZoneId, out _);
            Logger.Info("Zone registry remove zoneId={0} session={1}; loadedCount={2}",
                connection.ZoneId, sessionId, LoadedCount);
        }
    }

    /// <summary>
    /// Bind connection under its ZoneId after ZWJoin (and refresh on ZoneLoaded).
    /// Duplicate ZoneId replaces the previous mapping (warn).
    /// </summary>
    public void IndexByZoneId(ZoneConnection connection)
    {
        if (connection.ZoneId == 0)
            return;

        _byZoneId.AddOrUpdate(
            connection.ZoneId,
            connection,
            (_, existing) =>
            {
                if (!ReferenceEquals(existing, connection))
                {
                    Logger.Warn(
                        "ZoneId {0} remapped session {1} → {2} (previous dropped from registry index)",
                        connection.ZoneId, existing.Id, connection.Id);
                }

                return connection;
            });
    }

    public ZoneConnection? Get(uint sessionId) =>
        _bySession.TryGetValue(sessionId, out var connection) ? connection : null;

    /// <summary>ZoneLoaded connection for this zone key, or null.</summary>
    public ZoneConnection? GetByZoneId(uint zoneId)
    {
        if (zoneId == 0)
            return null;
        if (!_byZoneId.TryGetValue(zoneId, out var connection))
            return null;
        return connection.State >= ZoneConnectionState.ZoneLoaded ? connection : null;
    }

    /// <summary>Joined (or loaded) connection for zone key — not enter-ready unless ZoneLoaded.</summary>
    public ZoneConnection? GetJoinedByZoneId(uint zoneId)
    {
        if (zoneId == 0)
            return null;
        if (!_byZoneId.TryGetValue(zoneId, out var connection))
            return null;
        return connection.State >= ZoneConnectionState.Joined ? connection : null;
    }

    public IEnumerable<ZoneConnection> All => _bySession.Values;

    public int LoadedCount => _byZoneId.Values.Count(z => z.State >= ZoneConnectionState.ZoneLoaded);
}
