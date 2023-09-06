using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TaggedItem
{
    public long? Id { get; set; }

    public long? TagId { get; set; }

    public long? ItemId { get; set; }
}
