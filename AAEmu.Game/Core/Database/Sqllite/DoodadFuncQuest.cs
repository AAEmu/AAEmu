using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncQuest
{
    public long? Id { get; set; }

    public long? QuestKindId { get; set; }

    public long? QuestId { get; set; }
}
