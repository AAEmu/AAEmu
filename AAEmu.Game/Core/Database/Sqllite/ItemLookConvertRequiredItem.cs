using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemLookConvertRequiredItem
{
    public long? Id { get; set; }

    public long? ItemLookConvertId { get; set; }

    public long? ItemId { get; set; }

    public long? ItemCount { get; set; }
}
