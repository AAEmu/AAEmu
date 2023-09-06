using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DefaultInventoryTab
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? TabOrder { get; set; }

    public long? IconIdx { get; set; }
}
