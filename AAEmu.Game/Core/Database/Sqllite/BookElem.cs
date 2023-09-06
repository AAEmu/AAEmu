using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BookElem
{
    public long? Id { get; set; }

    public long? BookId { get; set; }

    public long? BookPageId { get; set; }
}
