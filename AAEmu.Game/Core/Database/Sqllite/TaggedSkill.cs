using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TaggedSkill
{
    public long? Id { get; set; }

    public long? TagId { get; set; }

    public long? SkillId { get; set; }
}
