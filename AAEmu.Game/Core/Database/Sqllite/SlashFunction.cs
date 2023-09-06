using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlashFunction
{
    public long? Id { get; set; }

    public long? SlashFuncId { get; set; }

    public string Comments { get; set; }
}
