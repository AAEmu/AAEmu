using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BuffMountSkill
{
    public long? Id { get; set; }

    public long? BuffId { get; set; }

    public long? MountSkillId { get; set; }
}
