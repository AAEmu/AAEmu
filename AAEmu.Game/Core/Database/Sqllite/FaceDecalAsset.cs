using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FaceDecalAsset
{
    public long? Id { get; set; }

    public long? CategoryId { get; set; }

    public string Name { get; set; }

    public string AssetPath { get; set; }

    public long? DefaultX { get; set; }

    public long? DefaultY { get; set; }

    public string Comment { get; set; }

    public long? ModelId { get; set; }

    public byte[] Movable { get; set; }

    public byte[] NpcOnly { get; set; }

    public long? DisplayOrder { get; set; }

    public byte[] IsHot { get; set; }

    public byte[] IsNew { get; set; }

    public string IconPath { get; set; }
}
