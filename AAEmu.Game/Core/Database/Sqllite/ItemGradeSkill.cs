using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemGradeSkill
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? ItemGradeId { get; set; }

    public long? SkillId { get; set; }
}
