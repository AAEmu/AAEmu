using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestTaskQuest
{
    public long? Id { get; set; }

    public long? QuestTaskId { get; set; }

    public long? QuestId { get; set; }
}
