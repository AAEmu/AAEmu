using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GameScheduleQuest
{
    public long? Id { get; set; }

    public long? GameScheduleId { get; set; }

    public long? QuestId { get; set; }
}
