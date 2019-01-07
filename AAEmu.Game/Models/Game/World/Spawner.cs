namespace AAEmu.Game.Models.Game.World
{
    public class Spawner<T> where T : class
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public Point Position { get; set; }

        public virtual T Spawn(uint objId)
        {
            return null;
        }

        public virtual void Despawn(T obj)
        {
        }
    }
}