using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FxGroupFxItem
{
    public long? Id { get; set; }

    public long? FxGroupId { get; set; }

    public long? FxItemId { get; set; }
}
