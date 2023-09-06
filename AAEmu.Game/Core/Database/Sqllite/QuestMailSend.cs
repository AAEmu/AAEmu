using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestMailSend
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CategoryId { get; set; }

    public long? QuestMailId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public long? Level { get; set; }

    public long? ActabilityGroupId { get; set; }

    public long? ActabilityPoint { get; set; }

    public long? QuestId { get; set; }

    public long? ComponentId { get; set; }

    public long? SphereId { get; set; }
}
