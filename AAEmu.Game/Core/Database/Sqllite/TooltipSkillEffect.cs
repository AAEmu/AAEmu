using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TooltipSkillEffect
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? EffectId { get; set; }
}
