using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Projectile
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ProjPhysicId { get; set; }

    public long? FxGroupId { get; set; }

    public long? DestBoneId { get; set; }

    public long? SrcBoneId { get; set; }

    public byte[] IsPermanent { get; set; }

    public byte[] IgnoreZRotation { get; set; }
}
