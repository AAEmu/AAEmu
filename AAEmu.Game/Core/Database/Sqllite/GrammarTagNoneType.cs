using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GrammarTagNoneType
{
    public long? Id { get; set; }

    public string Macrotag { get; set; }

    public string Locale { get; set; }
}
