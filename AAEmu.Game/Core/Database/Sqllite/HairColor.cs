using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class HairColor
{
    public long? Id { get; set; }

    public long? HairDiffuseColorR { get; set; }

    public long? HairDiffuseColorG { get; set; }

    public long? HairDiffuseColorB { get; set; }

    public long? ModelId { get; set; }

    public string HairMaterial { get; set; }

    public string Comment { get; set; }

    public long? AssetId { get; set; }

    public long? HairBaseColorR { get; set; }

    public long? HairBaseColorG { get; set; }

    public long? HairBaseColorB { get; set; }

    public byte[] NpcOnly { get; set; }
}
