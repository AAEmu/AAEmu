using AAEmu.Commons.Utils;
using AAEmu.World.Core.Network;

namespace AAEmu.World.Core.Zone;

/// <summary>
/// All TCP Zone connections. Indexed by session id and by (zoneId, instanceId).
/// </summary>
public class ZoneSession : Singleton<ZoneSession>
{
    private readonly ZoneConnectionRegistry _registry = new();

    public void Add(ZoneConnection connection) => _registry.Add(connection);

    public void Remove(uint sessionId) => _registry.Remove(sessionId);

    /// <summary>
    /// Bind connection after ZWJoin (and refresh on ZoneLoaded). Key is zone id plus join instance id.
    /// </summary>
    public void IndexByZoneId(ZoneConnection connection) => _registry.Index(connection);

    public ZoneConnection? Get(uint sessionId) => _registry.Get(sessionId);

    /// <summary>ZoneLoaded connection for this exact copy, or null.</summary>
    public ZoneConnection? GetByZoneInstance(uint zoneId, uint instanceId) =>
        _registry.GetLoaded(zoneId, instanceId);

    /// <summary>
    /// Unique ZoneLoaded host for a zone key. Several copies of the same dungeon return instance 0
    /// if present, otherwise null.
    /// </summary>
    public ZoneConnection? GetByZoneId(uint zoneId) => _registry.GetUniqueLoaded(zoneId);

    /// <summary>Joined (or loaded) connection for this copy.</summary>
    public ZoneConnection? GetJoinedByZoneInstance(uint zoneId, uint instanceId) =>
        _registry.GetJoined(zoneId, instanceId);

    /// <summary>Joined unique host for a zone key — not enter-ready unless ZoneLoaded.</summary>
    public ZoneConnection? GetJoinedByZoneId(uint zoneId) =>
        _registry.GetUnique(zoneId, ZoneConnectionState.Joined);

    public IEnumerable<ZoneConnection> All => _registry.All;

    public int LoadedCount => _registry.LoadedCount;
}
