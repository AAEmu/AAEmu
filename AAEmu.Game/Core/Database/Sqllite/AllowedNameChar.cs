using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AllowedNameChar
{
    public long? Id { get; set; }

    public string Char { get; set; }

    public long? Bytes { get; set; }
}
