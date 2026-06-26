namespace AAEmu.Game.Models.Game.Models;

public class ShipModelV1 : Model
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
    // 10.0.2.13: Accel/ReverseAccel removed (accel/reverse_accel columns absent from ship_models)
    public float ReverseVelocity { get; set; }
    public float TurnAccel { get; set; }
    public float TubeLength { get; set; }
    public float TubeRadius { get; set; }
    public float TubeOffsetZ { get; set; }
    public float KeelLength { get; set; }
    public float KeelHeight { get; set; }
    public float KeelOffsetZ { get; set; }
}
