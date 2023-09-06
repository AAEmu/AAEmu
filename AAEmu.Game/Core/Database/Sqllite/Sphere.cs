using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Sphere
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] EnterOrLeave { get; set; }

    public long? SphereDetailId { get; set; }

    public string SphereDetailType { get; set; }

    public long? TriggerConditionId { get; set; }

    public long? TriggerConditionTime { get; set; }

    public string TeamMsg { get; set; }

    public long? CategoryId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public byte[] IsPersonalMsg { get; set; }

    public long? MilestoneId { get; set; }

    public byte[] NameTr { get; set; }

    public byte[] TeamMsgTr { get; set; }
}
