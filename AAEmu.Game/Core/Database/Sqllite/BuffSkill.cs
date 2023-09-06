using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BuffSkill
{
    public long? Id { get; set; }

    public long? BuffId { get; set; }

    public long? SkillId { get; set; }
}
