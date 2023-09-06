using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RankScopeLink
{
    public long? Id { get; set; }

    public long? RankId { get; set; }

    public long? RankScopeId { get; set; }
}
