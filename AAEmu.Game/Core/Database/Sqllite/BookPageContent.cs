using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BookPageContent
{
    public long? Id { get; set; }

    public long? BookPageId { get; set; }

    public string Text { get; set; }

    public string Illust { get; set; }
}
