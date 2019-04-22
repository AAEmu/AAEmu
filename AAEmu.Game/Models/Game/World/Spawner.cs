namespace AAEmu.Game.Models.Game.World
{
    public class Spawner<T> where T : GameObject
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public Point Position { get; set; }
        public int RespawnTime { get; set; }
        public int DespawnTime { get; set; }

        public virtual T Spawn(uint objId)
        {
            return null;
        }

        public virtual void Respawn(T obj)
        {
            Spawn(obj.ObjId);
        }

        public virtual void Despawn(T obj)
        {
        }
    }
}
