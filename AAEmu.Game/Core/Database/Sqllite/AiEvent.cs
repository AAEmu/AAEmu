using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AiEvent
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? NpcId { get; set; }

    public long? IgnoreCategoryId { get; set; }

    public double? IgnoreTime { get; set; }

    public long? SkillId { get; set; }

    public byte[] OrUnitReqs { get; set; }
}
