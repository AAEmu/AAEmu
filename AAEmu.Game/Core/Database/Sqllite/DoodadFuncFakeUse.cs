using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncFakeUse
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? FakeSkillId { get; set; }

    public byte[] TargetParent { get; set; }
}
