using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.World;

public class Spawner<T> where T : GameObject
{
    public uint Id { get; set; }     // index
    public uint SpawnerId { get; set; } // spawner template id
    public uint UnitId { get; set; }    // npc template id
    public string FollowPath { get; set; } = string.Empty;
    public uint FollowNpc { get; set; } = 0; // nearest Npc TemplateId to follow
    public WorldSpawnPosition Position { get; set; }
    public int RespawnTime { get; set; } = 15;
    public int DespawnTime { get; set; } = 20;

    public virtual T Spawn(uint objId)
    {
        return null;
    }

    public virtual T Spawn(uint objId, ulong itemId, uint charId)
    {
        return null;
    }

    public virtual void Respawn(T obj)
    {
        Spawn(0);
    }

    public virtual void Despawn(T obj)
    {
    }
}
