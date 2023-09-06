using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemConvReagent
{
    public long? Id { get; set; }

    public long? ItemConvRpackId { get; set; }

    public long? ItemId { get; set; }

    public long? GradeId { get; set; }

    public long? MaxGradeId { get; set; }
}
