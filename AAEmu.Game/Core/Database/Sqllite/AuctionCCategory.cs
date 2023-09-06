using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AuctionCCategory
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ParentCategoryId { get; set; }
}
