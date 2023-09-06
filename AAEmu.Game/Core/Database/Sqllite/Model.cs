using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Model
{
    public long? Id { get; set; }

    public string Comment { get; set; }

    public long? SubId { get; set; }

    public string SubType { get; set; }

    public double? DyingTime { get; set; }

    public long? SoundMaterialId { get; set; }

    public byte[] Big { get; set; }

    public double? TargetDecalSize { get; set; }

    public byte[] UseTargetDecal { get; set; }

    public byte[] UseTargetSilhouette { get; set; }

    public byte[] UseTargetHighlight { get; set; }

    public string Name { get; set; }

    public double? CameraDistance { get; set; }

    public byte[] ShowNameTag { get; set; }

    public double? NameTagOffset { get; set; }

    public long? SoundPackId { get; set; }

    public byte[] DespawnDoodadOnCollision { get; set; }

    public byte[] PlayMountAnimation { get; set; }

    public byte[] Selectable { get; set; }

    public long? MountPoseId { get; set; }

    public double? CameraDistanceForWideAngle { get; set; }
}
