using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillReqSkillTag
{
    public long? Id { get; set; }

    public long? SkillReqId { get; set; }

    public long? SkillTagId { get; set; }
}
