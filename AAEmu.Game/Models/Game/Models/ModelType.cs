namespace AAEmu.Game.Models.Game.Models;

public class ModelType
{
    public uint Id { get; set; }

public bool UseTargetSilhouette { get; set; }

public bool UseTargetHighlight { get; set; }

public bool UseTargetDecal { get; set; }

public float TargetDecalSize { get; set; }

public int SoundPackId { get; set; }

public int SoundMaterialId { get; set; }

public bool ShowNameTag { get; set; }

public bool Selectable { get; set; }

public bool PlayerMountNameTagPos { get; set; }

public bool PlayMountAnimation { get; set; }

public float NameTagOffset { get; set; }

public string Name { get; set; }

public int MountPoseId { get; set; }

public int MiddleImpactFxGroupId { get; set; }

public int LowImpactFxGroupId { get; set; }

public int HighImpactFxGroupId { get; set; }

public float DyingTime { get; set; }

public bool DespawnDoodadOnCollision { get; set; }

public float CameraDistanceForWideAngle { get; set; }

public float CameraDistance { get; set; }

public bool Big { get; set; }
    public string SubType { get; set; }
    public uint SubId { get; set; }
}
