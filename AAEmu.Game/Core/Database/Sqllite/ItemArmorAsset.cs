using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemArmorAsset
{
    public long? Id { get; set; }

    public long? ArmorAssetId { get; set; }

    public long? AssetId { get; set; }
}
