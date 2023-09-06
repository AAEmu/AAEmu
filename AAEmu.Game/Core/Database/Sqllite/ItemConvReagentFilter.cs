using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemConvReagentFilter
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ItemConvRpackId { get; set; }

    public long? ItemImplId { get; set; }

    public long? MinLevel { get; set; }

    public long? MaxLevel { get; set; }

    public long? ItemGradeId { get; set; }

    public long? MaxItemGradeId { get; set; }
}
