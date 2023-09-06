using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SpecialtyBundleItem
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? SpecialtyBundleId { get; set; }

    public long? Profit { get; set; }

    public long? Ratio { get; set; }
}
