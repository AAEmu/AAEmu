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
        public float Value1 { get; set; }
        public float Value2 { get; set; }
        public float Value3 { get; set; }
    }
}
