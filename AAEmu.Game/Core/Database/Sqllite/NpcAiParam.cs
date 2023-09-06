using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcAiParam
{
    public long? Id { get; set; }

    public string AiParam { get; set; }
}
