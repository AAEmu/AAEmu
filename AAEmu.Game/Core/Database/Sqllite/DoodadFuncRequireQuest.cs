using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncRequireQuest
{
    public long? Id { get; set; }

    public long? WiId { get; set; }

    public long? QuestId { get; set; }
}
