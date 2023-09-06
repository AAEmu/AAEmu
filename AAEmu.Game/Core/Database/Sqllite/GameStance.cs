using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GameStance
{
    public long? Id { get; set; }

    public long? StanceId { get; set; }

    public double? NormalSpeed { get; set; }

    public double? MaxSpeed { get; set; }

    public double? HeightCollider { get; set; }

    public double? HeightPivot { get; set; }

    public double? SizeX { get; set; }

    public double? SizeY { get; set; }

    public double? SizeZ { get; set; }

    public double? ModelOffsetX { get; set; }

    public double? ModelOffsetY { get; set; }

    public double? ModelOffsetZ { get; set; }

    public double? ViewOffsetX { get; set; }

    public double? ViewOffsetY { get; set; }

    public double? ViewOffsetZ { get; set; }

    public string Name { get; set; }

    public byte[] UseCapsule { get; set; }

    public long? ActorModelId { get; set; }

    public double? AiMoveSpeedSlow { get; set; }

    public double? AiMoveSpeedWalk { get; set; }

    public double? AiMoveSpeedRun { get; set; }

    public double? AiMoveSpeedSprint { get; set; }
}
