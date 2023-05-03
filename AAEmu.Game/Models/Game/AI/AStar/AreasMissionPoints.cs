namespace AAEmu.Game.Models.Game.AI.AStar
{
public class AreasMissionPoints
{
        public AreasMissionPoints()
        {
        }
        
        public AreasMissionPoints(uint id, uint zoneKey, uint startPoint, uint endPoint, float x, float y, float z)
        {
            Position = new Point();
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Id = id;
            ZoneKey = zoneKey;
        }
        
        public AreasMissionPoints(float x, float y, float z)
        {
            Position = new Point();
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Id = 0;
            ZoneKey = 0;
        }

        public uint Id { get; set; }
        public uint ZoneKey { get; set; }
        public Point Position { get; set; }
    }
}
