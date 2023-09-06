using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SphereQuest
{
    public long? Id { get; set; }

    public long? QuestId { get; set; }

    public long? QuestTriggerId { get; set; }
}
