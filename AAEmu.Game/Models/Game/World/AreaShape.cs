namespace AAEmu.Game.Models.Game.World
{
    public enum AreaShapeType
    {
        Sphere = 1,
        Cuboid = 2
    }
    public class AreaShape
    {
        public uint Id { get; set; }
        public AreaShapeType Type { get; set; }
        public double Value1 { get; set; }
        public double Value2 { get; set; }
        public double Value3 { get; set; }
    }
}
