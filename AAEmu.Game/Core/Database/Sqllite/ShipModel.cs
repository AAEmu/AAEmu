using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ShipModel
{
    public long? Id { get; set; }

    public string Normal { get; set; }

    public double? Velocity { get; set; }

    public double? Mass { get; set; }

    public double? MassCenterX { get; set; }

    public double? MassCenterY { get; set; }

    public double? MassCenterZ { get; set; }

    public double? MassBoxSizeX { get; set; }

    public double? MassBoxSizeY { get; set; }

    public double? MassBoxSizeZ { get; set; }

    public double? WaterDamping { get; set; }

    public double? WaterDensity { get; set; }

    public double? WaterResistance { get; set; }

    public string Damaged50 { get; set; }

    public string Dead { get; set; }

    public string Damaged25 { get; set; }

    public string Damaged75 { get; set; }

    public double? TubeLength { get; set; }

    public double? TubeRadius { get; set; }

    public double? TubeOffsetZ { get; set; }

    public double? KeelLength { get; set; }

    public double? KeelHeight { get; set; }

    public double? KeelOffsetZ { get; set; }

    public double? SteerVel { get; set; }

    public double? Accel { get; set; }

    public double? ReverseAccel { get; set; }

    public double? ReverseVelocity { get; set; }

    public double? TurnAccel { get; set; }
}
