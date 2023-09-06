using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class TowerDef
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string StartMsg { get; set; }

    public string EndMsg { get; set; }

    public double? Tod { get; set; }

    public double? FirstWaveAfter { get; set; }

    public long? TargetNpcSpawnerId { get; set; }

    public long? KillNpcId { get; set; }

    public long? KillNpcCount { get; set; }

    public double? ForceEndTime { get; set; }

    public long? TodDayInterval { get; set; }

    public string TitleMsg { get; set; }

    public long? MilestoneId { get; set; }
}
