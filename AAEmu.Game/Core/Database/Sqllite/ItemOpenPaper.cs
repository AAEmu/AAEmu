using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemOpenPaper
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? BookPageId { get; set; }

    public long? BookId { get; set; }
}
