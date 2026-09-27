using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Tracks the live instances of NPC templates listed by the client's <c>monitor_npcs</c> data.
/// </summary>
[GameData]
public class MonitorNpcGameData : Singleton<MonitorNpcGameData>, IGameDataLoader
{
    private readonly object _sync = new();
    private readonly Action<uint, bool> _publish;
    private HashSet<uint> _templates = [];
    private readonly Dictionary<uint, uint> _templateByObjectId = [];
    private readonly Dictionary<uint, int> _liveCountByTemplate = [];

    public MonitorNpcGameData()
        : this((templateId, spawned) =>
            WorldIntegration.BroadcastPacket(new SCMonitorNpcSpawnedPacket(templateId, spawned)))
    {
    }

    public MonitorNpcGameData(Action<uint, bool> publish)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
    }

    public void Load(SqliteConnection connection)
    {
        var templates = new HashSet<uint>();
        using var command = connection.CreateCommand();
        // Template ids the client lists in its monitor_npcs table.
        command.CommandText = "SELECT npc_id FROM monitor_npcs";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var npcId = reader.GetUInt32("npc_id");
            if (npcId != 0)
                templates.Add(npcId);
        }

        lock (_sync)
        {
            _templates = templates;
            _templateByObjectId.Clear();
            _liveCountByTemplate.Clear();
        }
    }

    public void PostLoad()
    {
    }

    /// <summary>Record a successfully-created live NPC mirror and publish the first live edge.</summary>
    public void OnSpawn(uint objectId, uint templateId)
    {
        if (objectId == 0 || templateId == 0)
            return;

        lock (_sync)
        {
            if (_templateByObjectId.TryGetValue(objectId, out var previous))
            {
                if (previous == templateId)
                    return;
                _templateByObjectId.Remove(objectId);
                if (Decrement(previous))
                    _publish(previous, false);
            }

            if (_templates.Contains(templateId))
            {
                _templateByObjectId[objectId] = templateId;
                _liveCountByTemplate.TryGetValue(templateId, out var count);
                _liveCountByTemplate[templateId] = count + 1;
                if (count == 0)
                    _publish(templateId, true);
            }
        }
    }

    /// <summary>Forget a live mirror and publish the last-live-instance edge.</summary>
    public void OnRemove(uint objectId)
    {
        lock (_sync)
        {
            if (!_templateByObjectId.Remove(objectId, out var templateId) || !Decrement(templateId))
                return;
            _publish(templateId, false);
        }
    }

    /// <summary>Current live template ids for a request made after login or UI reload.</summary>
    public uint[] GetSpawnedTemplates()
    {
        lock (_sync)
            return _liveCountByTemplate.Keys.Order().ToArray();
    }

    /// <summary>
    /// Publish a late-join snapshot in the same order domain as spawn/despawn edges.
    /// </summary>
    public void PublishSnapshot(Action<uint[]> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (_sync)
            publish(_liveCountByTemplate.Keys.Order().ToArray());
    }

    private bool Decrement(uint templateId)
    {
        if (!_liveCountByTemplate.TryGetValue(templateId, out var count))
            return false;
        if (count > 1)
        {
            _liveCountByTemplate[templateId] = count - 1;
            return false;
        }

        _liveCountByTemplate.Remove(templateId);
        return true;
    }
}
