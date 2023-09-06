using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FaceNormalMap
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public string Name { get; set; }

    public string Normal { get; set; }

    public string Specular { get; set; }

    public byte[] NpcOnly { get; set; }

    public long? DisplayOrder { get; set; }

    public byte[] IsHot { get; set; }

    public byte[] IsNew { get; set; }

    public string IconPath { get; set; }
}
