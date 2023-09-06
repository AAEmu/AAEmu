using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RaceTrack
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ZoneId { get; set; }

    public long? RaceLoop { get; set; }

    public long? RecordMin { get; set; }

    public long? RecordMax { get; set; }

    public long? DoodadId { get; set; }

    public long? WaitDelay { get; set; }

    public long? ReadyDelay { get; set; }

    public long? StartDelay { get; set; }

    public long? DoodadGroupId { get; set; }

    public long? ReadyNpcId { get; set; }

    public long? ReadyBuffId { get; set; }

    public long? StartNpcId { get; set; }

    public long? StartBuffId { get; set; }

    public long? EndNpcId { get; set; }

    public long? EndBuffId { get; set; }
}
