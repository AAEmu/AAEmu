using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncSpawnGimmick
{
    public long? Id { get; set; }

    public long? GimmickId { get; set; }

    public long? FactionId { get; set; }

    public double? Scale { get; set; }

    public double? OffsetX { get; set; }

    public double? OffsetY { get; set; }

    public double? OffsetZ { get; set; }

    public double? VelocityX { get; set; }

    public double? VelocityY { get; set; }

    public double? VelocityZ { get; set; }

    public double? AngleX { get; set; }

    public double? AngleY { get; set; }

    public double? AngleZ { get; set; }

    public long? NextPhase { get; set; }
}
