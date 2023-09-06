using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CraftPackCraft
{
    public long? Id { get; set; }

    public long? CraftPackId { get; set; }

    public long? CraftId { get; set; }
}
