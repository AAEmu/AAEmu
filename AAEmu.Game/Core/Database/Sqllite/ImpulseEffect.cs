using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ImpulseEffect
{
    public long? Id { get; set; }

    public double? VelImpulseX { get; set; }

    public double? VelImpulseY { get; set; }

    public double? VelImpulseZ { get; set; }

    public double? AngvelImpulseX { get; set; }

    public double? AngvelImpulseY { get; set; }

    public double? AngvelImpulseZ { get; set; }

    public double? ImpulseX { get; set; }

    public double? ImpulseY { get; set; }

    public double? ImpulseZ { get; set; }

    public double? AngImpulseX { get; set; }

    public double? AngImpulseY { get; set; }

    public double? AngImpulseZ { get; set; }
}
