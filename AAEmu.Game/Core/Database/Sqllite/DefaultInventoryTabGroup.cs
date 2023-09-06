using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DefaultInventoryTabGroup
{
    public long? Id { get; set; }

    public long? DefaultInventoryTabId { get; set; }

    public long? ItemGroupId { get; set; }
}
