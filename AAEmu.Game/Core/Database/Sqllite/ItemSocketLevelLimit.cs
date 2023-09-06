using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemSocketLevelLimit
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ItemId { get; set; }

    public long? Level { get; set; }
}
