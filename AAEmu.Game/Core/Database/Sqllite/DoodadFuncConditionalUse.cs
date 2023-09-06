using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncConditionalUse
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? FakeSkillId { get; set; }

    public long? QuestId { get; set; }

    public long? QuestTriggerPhase { get; set; }

    public long? ItemId { get; set; }

    public long? ItemTriggerPhase { get; set; }
}
