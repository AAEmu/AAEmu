using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveMountSkill
{
    public long? Id { get; set; }

    public long? SlaveId { get; set; }

    public long? MountSkillId { get; set; }
}
