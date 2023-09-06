using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MountAttachedSkill
{
    public long? Id { get; set; }

    public long? MountSkillId { get; set; }

    public long? AttachPointId { get; set; }

    public long? SkillId { get; set; }
}
