using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.Slaves;

public class SlaveSpawner : Spawner<Slave>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<Slave> _spawned;
    public Slave _lastSpawn;
    private int _scheduledCount;
    private int _spawnCount;
    public uint Count { get; set; } = 1;
    public WorldInstance World { get; set; }

    public SlaveSpawner()
    {
        _spawned = [];
        Count = 1;
        _lastSpawn = new Slave();
    }

    public override Slave Spawn(uint objId)
    {
        DoSpawn();
        return _lastSpawn;
    }

    public override void Despawn(Slave slave)
    {
        slave.Delete();
        if (slave.Respawn == DateTime.MinValue)
        {
            _spawned.Remove(slave);
            ObjectIdManager.Instance.ReleaseId(slave.ObjId);
            _spawnCount--;
        }

        if (_lastSpawn == null || _lastSpawn.ObjId == slave.ObjId)
        {
            _lastSpawn = _spawned.Count != 0 ? _spawned[^1] : null;
        }
    }

    public void DecreaseCount(Slave npc)
    {
        _spawnCount--;
        _spawned.Remove(npc);
        if (RespawnTime > 0 && _spawnCount + _scheduledCount < Count)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
            World.SpawnManager.AddRespawn(npc);
            _scheduledCount++;
        }

        npc.Despawn = DateTime.UtcNow.AddSeconds(DespawnTime);
        npc.ParentWorld.SpawnManager.AddDespawn(npc);
    }

    private void DoSpawn()
    {
        var slave = World.SlaveManager.Create(null, this, 0);
        if (slave == null)
        {
            Logger.Warn($"Slave {UnitId}, from spawn not exist at db");
            return;
        }

        slave.ParentWorld = World;
        slave.Spawner = this;
        // slave.Transform.ApplyWorldSpawnPosition(Position);

        if (slave.Transform.World.IsOrigin())
        {
            Logger.Error($"Can't spawn slave {UnitId} from spawn {Id}");
            return;
        }

        _lastSpawn = slave;
    }
}
