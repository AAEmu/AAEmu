namespace AAEmu.Game.Models.Game.Models
{
    public class ShipModel : Model
    {
        public float Velocity { get; set; }
        public float Mass { get; set; }
        public float MassCenterX { get; set; }
        public float MassCenterY { get; set; }
        public float MassCenterZ { get; set; }
        public float MassBoxSizeX { get; set; }
        public float MassBoxSizeY { get; set; }
        public float MassBoxSizeZ { get; set; }
        public float WaterDensity { get; set; }
        public float WaterResistance { get; set; }
        public float SteerVel { get; set; }
        public float Accel { get; set; }
        public float ReverseAccel { get; set; }
        public float ReverseVelocity { get; set; }
        public float TurnAccel { get; set; }
        public float TubeLength { get; set; }
        public float TubeRadius { get; set; }
        public float TubeOffsetZ { get; set; }
        public float KeelLength { get; set; }
        public float KeelHeight { get; set; }
        public float KeelOffsetZ { get; set; }
    }
}
