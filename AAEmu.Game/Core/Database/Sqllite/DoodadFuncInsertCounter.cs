using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncInsertCounter
{
    public long? Id { get; set; }

    public long? Count { get; set; }

    public long? ItemId { get; set; }

    public long? ItemCount { get; set; }
}
