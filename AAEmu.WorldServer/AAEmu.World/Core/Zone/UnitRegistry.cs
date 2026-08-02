using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.World.Core.Zone;

/// <summary>
/// Per-zone unit body store. BcIds come from the process-wide
/// <see cref="ObjectIdManager"/> (same pool as characters) so multi-zone
/// mirrors stay unique and stay under dedicate <c>max_unit</c>.
/// </summary>
public class UnitRegistry
{
    private readonly ConcurrentDictionary<uint, byte[]> _units = new();

    public uint Register(byte[] rawBody)
    {
        var bcId = ObjectIdManager.Instance.GetNextId();
        _units[bcId] = rawBody;
        return bcId;
    }

    public void RegisterWithId(uint bcId, byte[] rawBody)
    {
        _units[bcId] = rawBody;
    }

    public bool TryRemove(uint bcId) => _units.TryRemove(bcId, out _);

    public bool TryGet(uint bcId, out byte[]? rawBody) => _units.TryGetValue(bcId, out rawBody);

    public bool Contains(uint bcId) => _units.ContainsKey(bcId);

    public int Count => _units.Count;

    public KeyValuePair<uint, byte[]>[] Snapshot() => _units.ToArray();

    /// <summary>Drop all tracked bodies (caller removes Game mirrors first).</summary>
    public int Clear()
    {
        var n = _units.Count;
        _units.Clear();
        return n;
    }
}
