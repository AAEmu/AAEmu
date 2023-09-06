using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class HousingGroup
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Desc { get; set; }

    public long? DoodadId { get; set; }

    public byte[] Houseless { get; set; }

    public long? ExistingCategoryId { get; set; }

    public long? AllowedTaxDelayWeek { get; set; }

    public byte[] CanExtend { get; set; }
}
