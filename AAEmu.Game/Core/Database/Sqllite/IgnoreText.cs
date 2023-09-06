using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IgnoreText
{
    public long? Id { get; set; }

    public string Utf8str { get; set; }

    public long? Bytes { get; set; }
}
