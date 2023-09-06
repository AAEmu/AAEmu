using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillReqSkill
{
    public long? Id { get; set; }

    public long? SkillReqId { get; set; }

    public long? SkillId { get; set; }
}
