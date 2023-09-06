using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class HousingDecoLimitElem
{
    public long? Id { get; set; }

    public long? HousingDecoLimitId { get; set; }

    public long? DecoActabilityGroupId { get; set; }

    public long? Count { get; set; }
}
