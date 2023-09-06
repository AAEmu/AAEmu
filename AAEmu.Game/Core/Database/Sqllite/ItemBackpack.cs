using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemBackpack
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? AssetId { get; set; }

    public long? BackpackTypeId { get; set; }

    public long? DeclareSiegeZoneGroupId { get; set; }

    public byte[] Heavy { get; set; }

    public long? Asset2Id { get; set; }

    public byte[] NormalSpecialty { get; set; }

    public byte[] UseAsStat { get; set; }

    public long? SkinKindId { get; set; }
}
