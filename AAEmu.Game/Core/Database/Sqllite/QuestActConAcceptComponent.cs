using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActConAcceptComponent
{
    public long? Id { get; set; }

    public long? QuestContextId { get; set; }
}
