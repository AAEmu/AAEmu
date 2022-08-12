namespace AAEmu.Game.Models.Game.AI.AStar
{
public class AreasMission
{
        public AreasMission()
        {
        }
        
        public uint Id { get; set; }
        public uint ZoneKey { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public uint PointCount { get; set; }
    }
}
