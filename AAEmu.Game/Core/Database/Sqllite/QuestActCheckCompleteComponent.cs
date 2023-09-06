using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActCheckCompleteComponent
{
    public long? Id { get; set; }

    public long? CompleteComponent { get; set; }
}
