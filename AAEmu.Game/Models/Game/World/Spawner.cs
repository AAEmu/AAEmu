using System.Collections.Generic;

using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.World
{
    public class Spawner<T> where T : GameObject
    {
        public uint Id { get; set; }     // objId or index ?
        public uint UnitId { get; set; } // MemberId | TemplateId
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
}
