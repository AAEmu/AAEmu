using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestCamera
{
    public long? Id { get; set; }

    public string Comment { get; set; }

    public double? Fov { get; set; }

    public double? NpcOffsetX { get; set; }

    public double? NpcOffsetY { get; set; }

    public double? NpcOffsetZ { get; set; }

    public double? CameraOffsetX { get; set; }

    public double? CameraOffsetY { get; set; }

    public double? CameraOffsetZ { get; set; }

    public byte[] Invisible { get; set; }

    public byte[] Interpolate { get; set; }

    public byte[] Dof { get; set; }

    public byte[] NvDof { get; set; }

    public double? NvBokehSize { get; set; }

    public double? NvIntensity { get; set; }

    public double? NvLuminance { get; set; }
}
