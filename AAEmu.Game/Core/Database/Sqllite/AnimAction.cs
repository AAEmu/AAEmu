using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AnimAction
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ActionStateId { get; set; }

    public long? MainhandToolId { get; set; }

    public long? OffhandToolId { get; set; }

    public string AnimName { get; set; }

    public byte[] NoRotate { get; set; }

    public string ModelPath { get; set; }

    public double? ModelPosX { get; set; }

    public double? ModelPosY { get; set; }

    public double? ModelPosZ { get; set; }

    public double? ModelAngle { get; set; }

    public byte[] ModelPhysic { get; set; }

    public long? MountPoseId { get; set; }
}
