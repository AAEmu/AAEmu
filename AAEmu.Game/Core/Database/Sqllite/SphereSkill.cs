using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SphereSkill
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? MaxRate { get; set; }

    public long? MinRate { get; set; }
}
