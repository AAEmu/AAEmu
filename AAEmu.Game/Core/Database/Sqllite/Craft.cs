using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Craft
{
    public long? Id { get; set; }

    public string Title { get; set; }

    public long? CastDelay { get; set; }

    public long? ToolId { get; set; }

    public long? SkillId { get; set; }

    public long? WiId { get; set; }

    public string Desc { get; set; }

    public long? MilestoneId { get; set; }

    public long? ReqDoodadId { get; set; }

    public byte[] NeedBind { get; set; }

    public long? AcId { get; set; }

    public long? ActabilityLimit { get; set; }

    public byte[] ShowUpperCrafts { get; set; }

    public long? RecommendLevel { get; set; }

    public long? VisibleOrder { get; set; }

    public byte[] Translate { get; set; }
}
