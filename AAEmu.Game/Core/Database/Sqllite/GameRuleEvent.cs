using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GameRuleEvent
{
    public long? Id { get; set; }

    public long? RuleSetId { get; set; }

    public long? ConditionId { get; set; }

    public long? KindId { get; set; }

    public long? Param1 { get; set; }

    public long? Param2 { get; set; }
}
