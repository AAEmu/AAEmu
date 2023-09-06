using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SphereAcceptQuestQuest
{
    public long? Id { get; set; }

    public long? SphereAcceptQuestId { get; set; }

    public long? QuestId { get; set; }
}
