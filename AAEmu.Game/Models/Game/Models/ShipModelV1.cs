namespace AAEmu.Game.Models.Game.Models;

public class ShipModelV1 : Model
{
    public float Velocity { get; set; }
    public float WaterDamping { get; set; }
    public float PassengerBoxScaleZ { get; set; }
    public float PassengerBoxScaleY { get; set; }
    public float PassengerBoxScaleX { get; set; }
    public float PassengerBoxOffsetZ { get; set; }
    public float PassengerBoxOffsetY { get; set; }
    public float PassengerBoxOffsetX { get; set; }
    public string Normal { get; set; }
    public float MinRpmSec { get; set; }
    public float MaxRpmSec { get; set; }
    public float ImpactMass { get; set; }
    public float HaltRate { get; set; }
    public string Dead { get; set; }
    public string Damaged75 { get; set; }
    public string Damaged50 { get; set; }
    public string Damaged25 { get; set; }
    public float CollisionSphereRadius { get; set; }
    public float CollisionBoxSizeZ { get; set; }
    public float CollisionBoxSizeY { get; set; }
    public float CollisionBoxSizeX { get; set; }
    public float CollisionBoxScaleZ { get; set; }
    public float CollisionBoxScaleY { get; set; }
    public float CollisionBoxScaleX { get; set; }
    public float CollisionBoxOffsetZ { get; set; }
    public float CollisionBoxOffsetY { get; set; }
    public float CollisionBoxOffsetX { get; set; }
    public float CollisionBoxCenterZ { get; set; }
    public float CollisionBoxCenterY { get; set; }
    public float CollisionBoxCenterX { get; set; }
    public int CharAnimSteerForwardId { get; set; }
    public int CharAnimSteerBackwardId { get; set; }
    public float AccelExponent { get; set; }
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
